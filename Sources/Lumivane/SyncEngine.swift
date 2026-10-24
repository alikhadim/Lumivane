import Foundation
import CoreVideo
import HueCore
import HueKit
import CaptureKit

struct Metrics: Sendable {
    var captureFPS = 0.0, captureMs = 0.0, reduceMs = 0.0, gpuMs = 0.0
    var endToEndMs = 0.0, sendHz = 0.0, cpuPercent = 0.0
    var audioToLightMs = 0.0
    var captureSessions = 0
    var mode = "Idle"
    var audio = AudioFeatures()
    var audioActive = false
}

/// Per-display capture (SCK) -> zone reduction (Metal, CPU fallback) -> per-light stats -> ColorEngine
///   + system-audio analysis (FFT) -> AudioMixer -> Entertainment stream (or v2 fallback).
/// Capture sessions exist only for sources a light actually follows. A fixed-rate timer drives output because
/// ScreenCaptureKit sends no frames for static content, and the bridge drops a stream that goes quiet.
final class SyncEngine: @unchecked Sendable {
    struct Config {
        var bridge: BridgeInfo
        var creds: Credentials
        var area: EntertainmentArea
        var lights: [HueLight]
        var settings: SyncSettings
        /// Demo Mode: synthetic frames and audio, nothing is captured or sent.
        var demo = false
    }

    var onUpdate: (@Sendable (Metrics, [Int: RGB]) -> Void)?
    var onError: (@Sendable (String) -> Void)?
    var onSourceLost: (@Sendable (String) -> Void)?
    var onCompare: (@Sendable ([ColorStyle: [Int: RGB]]) -> Void)?
    var compareEnabled: Bool { get { lk(); defer { ulk() }; return compareOn } set { lk(); compareOn = newValue; ulk() } }

    private let reducer = MetalReducer()
    private let color = ColorEngine()
    private let mixer = AudioMixer()
    private let lock = NSLock()
    private let queue = DispatchQueue(label: "lumivane.send", qos: .userInteractive)
    private var timer: DispatchSourceTimer?
    private var streamer: EntertainmentStreamer?
    private var client: HueClient?
    private var areaID = ""
    private var channels: [EntertainmentChannel] = []
    private var lightsByChannel: [Int: [HueLight]] = [:]

    // video
    private var captures: [String: ScreenCapturer] = [:]
    private var zones: [String: [ZoneRect]] = [:]
    private var refs: [Int: [(String, Int)]] = [:]
    private var driven: [Int] = []
    private var statsBySource: [String: [ZoneStats]] = [:]
    private var fresh: [String: Double] = [:]
    private var fps = 50
    private var demo = false, demoStart = 0.0

    // audio
    private let audioCapture = AudioCapturer()
    private var analyzer: AudioAnalyzer?
    private var audioInput: AudioInput?
    private var features = AudioFeatures()
    private var audioPending: Double?
    private var lastBeat: Float = 0

    // shared (lock)
    private var settings = SyncSettings()
    private var m = Metrics()
    private var frames = 0, sends = 0
    private var lastReport = hostSeconds(), lastTick = hostSeconds(), lastUI = 0.0, lastCPU = cpuSeconds()
    private var fallback = false, fbIndex = 0, fbLast = 0.0, fbBusy = false
    private var currentPlan = RoutePlan()
    private var lastAudioOnly: [Int] = []
    private var compareOn = false, lastCompare = 0.0
    private var compareEngines: [ColorStyle: ColorEngine] = [:]

    // Non-async wrappers keep NSLock calls out of async contexts.
    private func lk() { lock.lock() }
    private func ulk() { lock.unlock() }

    var isRunning: Bool { lk(); defer { ulk() }; return timer != nil }

    func update(_ s: SyncSettings) { lk(); settings = s; ulk() }

    func start(_ cfg: Config) async throws {
        demo = cfg.demo
        let client = demo ? nil : HueClient(bridge: cfg.bridge, credentials: cfg.creds)
        self.client = client
        areaID = cfg.area.id
        channels = cfg.area.channels
        lightsByChannel = Dictionary(uniqueKeysWithValues: channels.map { ch in
            (ch.id, cfg.lights.filter { l in l.entertainmentServiceID.map(ch.serviceIDs.contains) ?? false })
        })
        color.reset(); mixer.reset(); compareEngines = [:]
        lk(); settings = cfg.settings; fps = min(max(cfg.settings.fps, 10), 60)
        fallback = false; m = Metrics(); frames = 0; sends = 0; currentPlan = RoutePlan(); ulk()

        var mode = "Entertainment streaming"
        if demo { mode = "Entertainment streaming (demo)"; demoStart = hostSeconds() }
        else if let client { do {
            try await client.setStreaming(areaID: cfg.area.id, active: true)
            let appID = try await client.applicationID()
            guard let s = EntertainmentStreamer(host: cfg.bridge.ip, appID: appID, clientKeyHex: cfg.creds.clientKey, areaID: cfg.area.id)
            else { throw HueError.streamFailed("The stored key is invalid. Pair again in Settings.") }
            s.onFailure = { [weak self] msg in self?.onError?("Light stream interrupted: \(msg)") }
            try await s.start()
            streamer = s
        } catch {
            try? await client.setStreaming(areaID: cfg.area.id, active: false)
            fallback = true
            mode = "Basic mode (slower)"
            onError?("Smooth streaming unavailable, using basic mode. \(error.localizedDescription)")
        } }
        lk(); m.mode = mode; ulk()

        let t = DispatchSource.makeTimerSource(flags: .strict, queue: queue)
        let period = 1.0 / Double(fps)
        t.schedule(deadline: .now() + period, repeating: period, leeway: .milliseconds(1))
        t.setEventHandler { [weak self] in self?.tick() }
        lk(); lastTick = hostSeconds(); timer = t; ulk()
        t.resume()
    }

    /// Opens capture sessions for newly needed sources and closes ones no longer used. Safe to call repeatedly.
    func apply(plan: RoutePlan, targets: [String: CaptureTarget], audioOnlyChannels: [Int]) async {
        lk()
        let unchanged = plan == currentPlan && audioOnlyChannels == lastAudioOnly && (demo || Set(captures.keys) == plan.physicalSources)
        ulk()
        if unchanged { return }
        var z: [String: [ZoneRect]] = [:]
        var r: [Int: [(String, Int)]] = [:]
        for (ch, pts) in plan.samples.sorted(by: { $0.key < $1.key }) {
            for p in pts {
                var list = z[p.source] ?? []
                let idx = list.firstIndex(of: p.rect) ?? { list.append(p.rect); return list.count - 1 }()
                z[p.source] = list
                r[ch, default: []].append((p.source, idx))
            }
        }
        let needed = plan.physicalSources
        lk()
        zones = z; refs = r
        driven = plan.samples.isEmpty ? audioOnlyChannels : plan.samples.keys.sorted()
        statsBySource = statsBySource.filter { needed.contains($0.key) }
        currentPlan = plan; lastAudioOnly = audioOnlyChannels
        let rate = fps
        ulk()

        if demo { lk(); m.captureSessions = needed.count; ulk(); return }
        for (k, c) in captures where !needed.contains(k) { await c.stop(); captures[k] = nil }
        for k in needed where captures[k] == nil {
            guard let t = targets[k] else { onError?("A selected screen or window isn't available."); continue }
            let c = ScreenCapturer()
            c.onStop = { [weak self] _ in self?.onSourceLost?(k) }
            do {
                try await c.start(target: t, fps: rate) { [weak self] pb, latency in self?.handle(k, pb, latency) }
                captures[k] = c
            } catch { onError?("Couldn't capture that screen: \(error.localizedDescription)") }
        }
        lk(); m.captureSessions = captures.count; ulk()
    }

    /// Starts, stops or switches audio capture to match the profile.
    func applyAudio(_ a: AudioSettings) async {
        let want: AudioInput? = a.enabled && a.influence > 0 ? a.input : nil
        if demo { lk(); m.audioActive = want != nil; ulk(); return }
        if want == audioInput { return }
        audioInput = nil
        await audioCapture.stop()
        lk(); features = AudioFeatures(); m.audioActive = false; audioPending = nil; ulk()
        guard let input = want else { return }
        audioCapture.onStop = { [weak self] msg in self?.onError?("Audio capture stopped: \(msg)") }
        do {
            try await audioCapture.start(input) { [weak self] mono, rate, end in self?.handleAudio(mono, rate, end) }
            audioInput = input
            lk(); m.audioActive = true; ulk()
        } catch { onError?("Audio reactivity is off: \(error.localizedDescription)") }
    }

    func stop() async {
        lk(); let t = timer; timer = nil; ulk()
        t?.cancel()
        for (_, c) in captures { await c.stop() }
        captures = [:]
        audioInput = nil
        await audioCapture.stop()
        streamer?.stop(); streamer = nil
        if let c = client, !areaID.isEmpty { try? await c.setStreaming(areaID: areaID, active: false) }
        client = nil
        lk(); statsBySource = [:]; fresh = [:]; currentPlan = RoutePlan(); driven = []; ulk()
    }

    // MARK: video frames

    private func handle(_ source: String, _ pb: CVPixelBuffer, _ latencyMs: Double) {
        let t0 = hostSeconds()
        lk(); let rects = zones[source] ?? []; ulk()
        guard !rects.isEmpty else { return }
        var raw = reducer?.reduce(pb, zones: rects)
        if raw == nil {
            CVPixelBufferLockBaseAddress(pb, .readOnly)
            if let base = CVPixelBufferGetBaseAddress(pb) {
                raw = ZoneAccumulator.reduceBGRA(base, width: CVPixelBufferGetWidth(pb), height: CVPixelBufferGetHeight(pb),
                                                 bytesPerRow: CVPixelBufferGetBytesPerRow(pb), zones: rects)
            }
            CVPixelBufferUnlockBaseAddress(pb, .readOnly)
        }
        guard let raw else { return }
        let st = (0..<rects.count).map { ZoneAccumulator.stats(raw, zone: $0) }
        let t1 = hostSeconds()
        lk()
        statsBySource[source] = st
        if fresh[source] == nil { fresh[source] = t1 - latencyMs / 1000 }
        frames += 1; m.captureMs = latencyMs; m.reduceMs = (t1 - t0) * 1000; m.gpuMs = reducer?.lastGPUms ?? 0
        ulk()
    }

    // MARK: audio

    private func handleAudio(_ mono: [Float], _ rate: Double, _ end: Double) {
        if analyzer == nil || analyzer!.sampleRate != Float(rate) { analyzer = AudioAnalyzer(sampleRate: Float(rate)) }
        guard let a = analyzer, a.feed(mono) else { return }
        let f = a.features
        lk()
        features = f
        if audioPending == nil { audioPending = end }
        let onBeat = f.beat > lastBeat + 0.3
        lastBeat = f.beat
        ulk()
        if onBeat { queue.async { [weak self] in self?.tick(early: true) } }   // don't wait for the next timer slot
    }

    // MARK: output

    private func tick(early: Bool = false) {
        let now = hostSeconds()
        lk()
        if early && now - lastTick < 0.008 { ulk(); return }
        let dt = Float(now - lastTick); lastTick = now
        let s = settings, fb = fallback, ids = driven, refs = self.refs, cmp = compareOn
        var stats = statsBySource, feat = features
        let demoOn = demo, demoZones = zones
        let pendingAudio = audioPending; audioPending = nil
        let freshFrames = fresh; fresh = [:]
        ulk()
        guard !ids.isEmpty else { return }
        if demoOn {
            let t = Float(now - demoStart)
            stats = demoZones.reduce(into: [:]) { out, e in out[e.key] = e.value.map { DemoScene.stats(scene: DemoData.scene(forSource: e.key), rect: $0, t: t) } }
            feat = DemoSignal.audio(t: t)
        }

        let perChannel: [(id: Int, stats: ZoneStats)] = ids.map { id in
            let parts = (refs[id] ?? []).compactMap { (src, i) -> ZoneStats? in
                guard let arr = stats[src], i < arr.count else { return nil }
                return arr[i]
            }
            return (id, ZoneStats.combine(parts))
        }
        var colors = color.process(perChannel, dt: dt, settings: s)
        if cmp, now - lastCompare >= 0.2 {
            let cdt = Float(now - lastCompare); lastCompare = now
            var res: [ColorStyle: [Int: RGB]] = [:]
            for st in ColorStyle.allCases {
                let e = compareEngines[st] ?? ColorEngine(); compareEngines[st] = e
                var s2 = s; s2.style = st
                res[st] = e.process(perChannel, dt: cdt, settings: s2)
            }
            onCompare?(res)
        }
        if s.audio.enabled && s.audio.influence > 0 { colors = mixer.mix(screen: colors, order: ids, features: feat, dt: dt, settings: s) }

        if fb { fallbackSend(colors, now: now) }
        else if let streamer {
            streamer.send(ids.map { (UInt8($0), colors[$0] ?? .black) })
            let sent = hostSeconds()
            lk()
            sends += 1
            if let oldest = freshFrames.values.min() { m.endToEndMs = max(0, (sent - oldest) * 1000) }
            if let p = pendingAudio { m.audioToLightMs = max(0, (sent - p) * 1000) }
            ulk()
        }

        lk()
        if now - lastReport >= 0.5 {
            let span = now - lastReport
            m.captureFPS = Double(frames) / span; m.sendHz = Double(sends) / span
            frames = 0; sends = 0; lastReport = now
            let cpu = cpuSeconds(); m.cpuPercent = (cpu - lastCPU) / span * 100; lastCPU = cpu
        }
        m.audio = feat
        let emit = now - lastUI >= 0.1
        if emit { lastUI = now }
        let snapshot = m
        ulk()
        if emit { onUpdate?(snapshot, colors) }
    }

    /// v2 REST fallback: the bridge accepts roughly 10 light commands per second in total.
    private func fallbackSend(_ colors: [Int: RGB], now: Double) {
        lk()
        let ids = driven
        guard now - fbLast >= 0.11, !fbBusy, !ids.isEmpty else { ulk(); return }
        fbLast = now; fbBusy = true
        let chID = ids[fbIndex % ids.count]; fbIndex += 1
        ulk()
        guard let c = colors[chID], let client else { lk(); fbBusy = false; ulk(); return }
        let peak = max(c.r, c.g, c.b)
        let chroma = peak > 0 ? c.scaled(1 / peak) : RGB(1, 1, 1)
        let targets = lightsByChannel[chID] ?? []
        Task { [weak self] in
            for l in targets {
                try? await client.setLight(l.id, on: peak > 0.02, brightness: Double(peak) * 100, rgb: chroma, gamut: l.gamut, durationMs: 150)
            }
            self?.lk(); self?.fbBusy = false; self?.ulk()
        }
    }
}

private func cpuSeconds() -> Double {
    var u = rusage(); getrusage(RUSAGE_SELF, &u)
    func s(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
    return s(u.ru_utime) + s(u.ru_stime)
}
