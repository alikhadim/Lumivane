import SwiftUI
import ServiceManagement
import CoreGraphics
import HueCore
import HueKit
import CaptureKit

enum SetupStep: Int, CaseIterable { case welcome, bridge, pair, lights, area, permission, done }

struct SourceItem: Identifiable {
    enum Kind { case display(DisplayGeometry), window(WindowSource), desktop }
    let id: String
    let kind: Kind
    let connected: Bool
    var target: CaptureTarget?
}

@MainActor @Observable
final class AppModel {
    // setup
    var step: SetupStep = SetupStep(rawValue: UserDefaults.standard.integer(forKey: "setupStep")) ?? .welcome {
        didSet { if Store.persist { UserDefaults.standard.set(step.rawValue, forKey: "setupStep") } }
    }
    var onboarded = UserDefaults.standard.bool(forKey: "onboarded")
    var bridges: [BridgeInfo] = []
    var bridge: BridgeInfo? = Store.get("bridge")
    var credentials: Credentials? = Keychain.load("creds").flatMap { try? JSONDecoder().decode(Credentials.self, from: $0) }
    var searching = false
    var pairing = false
    var busy = false
    var error: String?
    var notice: String?

    /// Demo Mode: virtual bridge, lights, rooms and displays. Nothing is saved, captured or sent while it is on.
    var demo = false

    // Hue data
    var lights: [HueLight] = []
    var groups: [HueGroup] = []
    var scenes: [HueScene] = []
    var areas: [EntertainmentArea] = []
    var diagnostics: Diagnostics?

    // profiles: the active profile is the live working copy and autosaves
    var profiles: [Profile]
    var profile: Profile { didSet { profileChanged(old: oldValue) } }

    // displays and sources
    var displays: [DisplayGeometry] = []            // connected first, then remembered-but-disconnected
    var liveDisplays: [LiveDisplay] = []
    var windows: [LiveWindow] = []
    var thumbnails: [CaptureTarget: CGImage] = [:]
    private var knownDisplays: [String: DisplayGeometry] = Store.get("knownDisplays") ?? [:]
    private var activeSubstitutions: [Substitution] = []

    // audio
    var audioApps: [(bundleID: String, name: String)] = []

    // sync
    var syncing = false
    var metrics = Metrics()
    var preview: [Int: RGB] = [:]
    var styleCompare: [ColorStyle: [Int: RGB]] = [:]
    var networkMs: Double?
    var appearance = Appearance(rawValue: UserDefaults.standard.string(forKey: "appearance") ?? "") ?? .system {
        didSet { UserDefaults.standard.set(appearance.rawValue, forKey: "appearance") }
    }

    private let engine = SyncEngine()
    private var client: HueClient?
    private var pingTask: Task<Void, Never>?
    private var replanTask: Task<Void, Never>?
    private var screenObserver: NSObjectProtocol?

    var hasSetup: Bool { bridge != nil && credentials != nil }
    var settings: SyncSettings { get { profile.settings } set { profile.settings = newValue } }
    var selectedArea: EntertainmentArea? { areas.first { $0.id == profile.areaID } ?? areas.first }
    var screenAllowed: Bool { demo || CGPreflightScreenCaptureAccess() }
    var launchAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set { do { newValue ? try SMAppService.mainApp.register() : try SMAppService.mainApp.unregister() } catch { self.error = error.localizedDescription } }
    }

    init() {
        var stored: [Profile] = Store.get("profiles") ?? Profile.presets()
        if UserDefaults.standard.integer(forKey: "presetsVersion") < 2 {   // built-in profiles pick up the new color styles once
            let fresh = Dictionary(uniqueKeysWithValues: Profile.presets().map { ($0.name, $0.settings) })
            for i in stored.indices { if let f = fresh[stored[i].name] { stored[i].settings.style = f.style; stored[i].settings.intensity = f.intensity; stored[i].settings.gamma = f.gamma } }
            UserDefaults.standard.set(2, forKey: "presetsVersion")
        }
        let id: UUID? = Store.get("activeProfile")
        profiles = stored
        profile = stored.first { $0.id == id } ?? stored[0]
        engine.onUpdate = { [weak self] m, p in Task { @MainActor in self?.metrics = m; self?.preview = p; MetricsLog.shared?.write(m, networkMs: self?.networkMs) } }
        engine.onCompare = { [weak self] c in Task { @MainActor in self?.styleCompare = c } }
        engine.onError = { [weak self] msg in Task { @MainActor in self?.notice = msg } }
        engine.onSourceLost = { [weak self] _ in Task { @MainActor in self?.scheduleReplan(after: 0.5) } }
        screenObserver = NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.scheduleReplan(after: 0.8) }
        }
        engine.update(profile.settings)
        refreshDisplays()
        // `--demo` is for this launch only; the Settings toggle is what is remembered.
        if CommandLine.arguments.contains("--demo") { applyDemo(true, remember: false) }
        else if UserDefaults.standard.bool(forKey: "demoMode") { applyDemo(true) }
        // Connect and auto-start independent of any window, so the menu bar and launch options work with the window closed.
        Task { [weak self] in await self?.bootOnce() }
    }

    private var booted = false
    func bootOnce() async {
        guard !booted else { return }
        booted = true
        await boot()
    }

    // MARK: demo mode

    func setDemo(_ on: Bool) async {
        guard on != demo else { return }
        if syncing { await stop() }
        applyDemo(on)
        if !on { await boot() }
    }

    private func applyDemo(_ on: Bool, remember: Bool = true) {
        demo = on
        if remember { UserDefaults.standard.set(on, forKey: "demoMode") }
        Store.persist = !on
        error = nil; notice = nil; thumbnails = [:]; windows = []; audioApps = []; styleCompare = [:]; preview = [:]
        if on {
            client = nil
            bridge = DemoData.bridge; credentials = DemoData.credentials; onboarded = true
            lights = DemoData.lights; groups = DemoData.groups; scenes = DemoData.scenes; areas = DemoData.areas
            diagnostics = DemoData.diagnostics
            profiles = DemoData.profiles(); profile = profiles[0]
            audioApps = DemoData.audioApps; windows = DemoData.windows
            refreshDisplays()
        } else {
            client = nil
            bridge = Store.get("bridge")
            credentials = Keychain.load("creds").flatMap { try? JSONDecoder().decode(Credentials.self, from: $0) }
            onboarded = UserDefaults.standard.bool(forKey: "onboarded")
            lights = []; groups = []; scenes = []; areas = []; diagnostics = nil
            let stored: [Profile] = Store.get("profiles") ?? Profile.presets()
            let id: UUID? = Store.get("activeProfile")
            profiles = stored; profile = stored.first { $0.id == id } ?? stored[0]
            refreshDisplays()
        }
    }

    /// Live per-style preview on the Home page; costs a few microseconds per update while visible.
    func setCompare(_ on: Bool) { engine.compareEnabled = on; if !on { styleCompare = [:] } }

    func boot() async {
        if demo { if LaunchOptions.autoSync && !syncing { await start() }; return }
        if onboarded && !screenAllowed { requestScreenAccess(); watchScreenAccess() }
        guard hasSetup, let b = bridge else { return }
        client = HueClient(bridge: b, credentials: credentials)
        await refresh()
        refreshDisplays()
        if LaunchOptions.autoSync && !syncing { await start() }
    }

    // MARK: profiles

    private func profileChanged(old: Profile) {
        if let i = profiles.firstIndex(where: { $0.id == profile.id }) { profiles[i] = profile } else { profiles.append(profile) }
        Store.set("profiles", profiles); Store.set("activeProfile", profile.id)
        engine.update(profile.settings)
        guard syncing else { return }
        if old.areaID != profile.areaID {
            Task { await stop(); await start() }
        } else { scheduleReplan(after: 0.25) }
    }

    func load(_ p: Profile) {
        var p = p
        if let a = areas.first(where: { $0.id == p.areaID }) ?? selectedArea { p.areaID = a.id; p.ensureRoutes(for: a.channels) }
        profile = p
        notice = nil
    }

    func saveCurrentAsNew(named name: String) {
        var p = profile; p.id = UUID(); p.name = name.isEmpty ? "My profile" : name
        profile = p
    }

    func delete(_ p: Profile) {
        guard profiles.count > 1 else { return }
        profiles.removeAll { $0.id == p.id }
        Store.set("profiles", profiles)
        if profile.id == p.id { profile = profiles[0] }
    }

    func rename(_ p: Profile, to name: String) {
        if p.id == profile.id { profile.name = name }
        else if let i = profiles.firstIndex(where: { $0.id == p.id }) { profiles[i].name = name; Store.set("profiles", profiles) }
    }

    // MARK: displays, windows, sources

    func refreshDisplays() {
        if demo { liveDisplays = DemoData.liveDisplays; displays = liveDisplays.map(\.geometry); return }
        liveDisplays = DisplayCatalog.current()
        for d in liveDisplays { knownDisplays[d.geometry.key] = d.geometry }
        Store.set("knownDisplays", knownDisplays)
        let liveKeys = Set(liveDisplays.map(\.geometry.key))
        var remembered = knownDisplays.values.filter { !liveKeys.contains($0.key) }
        for i in remembered.indices { remembered[i].isConnected = false }
        displays = liveDisplays.map(\.geometry) + remembered.sorted { $0.name < $1.name }
    }

    func refreshWindows() async { windows = demo ? DemoData.windows : await WindowCatalog.current() }

    func refreshAudioApps() async { audioApps = demo ? DemoData.audioApps : await AudioCapturer.runningApps() }

    var sourceItems: [SourceItem] {
        var items = displays.map { d in
            SourceItem(id: d.id, kind: .display(d), connected: d.isConnected,
                       target: liveDisplays.first { $0.geometry.key == d.key }.map { .display($0.directID) })
        }
        if displays.contains(where: \.isConnected) { items.append(SourceItem(id: SourceID.desktop, kind: .desktop, connected: true, target: nil)) }
        for w in profile.windowSources {
            let live = windows.first { $0.id == w.id }
            items.append(SourceItem(id: w.id, kind: .window(w), connected: live != nil, target: live.map { .window($0.windowID) }))
        }
        return items
    }

    func sourceName(_ id: String) -> String {
        if id == SourceID.desktop { return "Entire desktop" }
        if let k = SourceID.displayKey(id) { return displays.first { $0.key == k }?.name ?? "Display" }
        return profile.windowSources.first { $0.id == id }.map { "\($0.appName): \($0.title)" } ?? "Window"
    }

    func isAvailable(_ id: String) -> Bool { sourceItems.first { $0.id == id }?.connected ?? false }

    func refreshThumbnails() async {
        if demo { thumbnails = DemoData.thumbnails(t: Float(Date().timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 3600))); return }
        let targets = sourceItems.compactMap(\.target)
        let shots = await Thumbnailer.capture(targets)
        if !shots.isEmpty { thumbnails.merge(shots) { _, new in new } }
    }

    func addWindow(_ w: LiveWindow) {
        guard !profile.windowSources.contains(where: { $0.id == w.id }) else { return }
        profile.windowSources.append(WindowSource(id: w.id, title: w.title, appName: w.appName))
    }
    func removeWindow(_ id: String) {
        profile.windowSources.removeAll { $0.id == id }
        profile.mirrorSources.removeAll { $0 == id }
        if profile.primarySource == id { profile.primarySource = nil }
        for i in profile.routes.indices where profile.routes[i].sourceID == id { profile.routes[i].sourceID = nil }
    }

    // MARK: assignments

    func setMode(_ m: SyncMode) { profile.mode = m }
    func setPrimary(_ id: String) { profile.primarySource = id }
    func toggleMirror(_ id: String) {
        if let i = profile.mirrorSources.firstIndex(of: id) { profile.mirrorSources.remove(at: i) } else { profile.mirrorSources.append(id) }
    }
    func isSelected(_ id: String) -> Bool {
        switch profile.mode {
        case .single: return effectivePrimary == id
        case .mirror: return profile.mirrorSources.contains(id)
        case .extend: return profile.routes.contains { $0.sourceID == id && $0.enabled }
        }
    }
    /// What single mode will actually use right now (includes the fallback when nothing was chosen).
    var effectivePrimary: String? {
        if let p = profile.primarySource { return p }
        return (displays.first { $0.isConnected && $0.isBuiltIn } ?? displays.first { $0.isConnected }).map(\.id)
    }

    func route(_ ch: Int) -> LightRoute? { profile.routes.first { $0.channelID == ch } }
    func updateRoute(_ ch: Int, _ f: (inout LightRoute) -> Void) {
        if let i = profile.routes.firstIndex(where: { $0.channelID == ch }) { f(&profile.routes[i]) }
    }
    func assign(_ ch: Int, to source: String?) { updateRoute(ch) { $0.sourceID = source; if source != nil { $0.enabled = true } } }

    /// Drag-and-drop payloads: `ch:<channel>` for one light, `grp:<id>` for a Hue room or zone.
    @discardableResult
    func drop(_ payload: String, onto source: String) -> Bool {
        var chans: [Int] = []
        if payload.hasPrefix("ch:"), let c = Int(payload.dropFirst(3)) { chans = [c] }
        else if payload.hasPrefix("grp:"), let g = groups.first(where: { $0.id == String(payload.dropFirst(4)) }), let area = selectedArea {
            chans = area.channels.filter { ch in lights.contains { l in g.lightIDs.contains(l.id) && (l.entertainmentServiceID.map(ch.serviceIDs.contains) ?? false) } }.map(\.id)
        }
        guard !chans.isEmpty else { return false }
        if profile.mode != .extend {
            profile.mode = .extend
            notice = "Switched to Extend mode so each light can follow its own display."
        }
        for c in chans { assign(c, to: source) }
        return true
    }

    func light(for ch: EntertainmentChannel) -> HueLight? {
        lights.first { l in l.entertainmentServiceID.map(ch.serviceIDs.contains) ?? false }
    }
    func lightName(_ ch: EntertainmentChannel) -> String { light(for: ch)?.name ?? "Light \(ch.id + 1)" }

    // MARK: planning and hot-plug

    private func buildPlan() -> (RoutePlan, [String: CaptureTarget], [Int]) {
        guard let area = selectedArea else { return (RoutePlan(), [:], []) }
        let ids = area.channels.map(\.id)
        let plan = RoutePlanner.plan(profile, channelIDs: ids, displays: displays, windows: Set(windows.map(\.id)))
        var targets: [String: CaptureTarget] = [:]
        for src in plan.physicalSources {
            if let k = SourceID.displayKey(src), let d = liveDisplays.first(where: { $0.geometry.key == k }) { targets[src] = .display(d.directID) }
            else if let w = windows.first(where: { $0.id == src }) { targets[src] = .window(w.windowID) }
        }
        let a = profile.settings.audio
        let audioOnly = a.enabled && a.influence >= 0.99 ? profile.routes.filter { $0.enabled && ids.contains($0.channelID) }.map(\.channelID) : []
        return (plan, targets, audioOnly)
    }

    func scheduleReplan(after delay: Double) {
        replanTask?.cancel()
        replanTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(delay * 1e9))
            guard !Task.isCancelled else { return }
            await self?.reconfigure()
        }
    }

    func reconfigure() async {
        refreshDisplays()
        if !profile.windowSources.isEmpty { await refreshWindows() }
        let (plan, targets, audioOnly) = buildPlan()
        announce(plan)
        guard syncing else { return }
        await engine.apply(plan: plan, targets: targets, audioOnlyChannels: audioOnly)
        await engine.applyAudio(profile.settings.audio)
    }

    /// Tells the user when a display went away (and what took over) or came back. Mappings are never rewritten.
    private func announce(_ plan: RoutePlan) {
        let now = Set(plan.substitutions), before = Set(activeSubstitutions)
        for s in now.subtracting(before) {
            let msg = "\(sourceName(s.requested)) is not available. Using \(sourceName(s.actual)) instead."
            notice = msg; Notifier.post("Display disconnected", msg)
        }
        for s in before.subtracting(now) where isAvailable(s.requested) {
            let msg = "\(sourceName(s.requested)) is back. Your lights follow it again."
            notice = msg; Notifier.post("Display reconnected", msg)
        }
        activeSubstitutions = plan.substitutions
    }

    // MARK: discovery and pairing

    func findBridges(online: Bool = false) async {
        searching = true; error = nil
        bridges = await BridgeDiscovery.discover(allowCloud: online)
        searching = false
        if bridges.count == 1 { select(bridges[0]) }
    }

    func addManual(ip: String) async {
        searching = true; error = nil
        if let p = await HueClient.probe(ip: ip.trimmingCharacters(in: .whitespaces)) {
            bridges = [p.info]; select(p.info)
        } else { error = "No Hue Bridge answered at that address." }
        searching = false
    }

    func select(_ b: BridgeInfo) {
        bridge = b; Store.set("bridge", b)
        client = HueClient(bridge: b, credentials: nil)
    }

    func pair() async {
        guard let b = bridge else { return }
        pairing = true; error = nil
        let c = HueClient(bridge: b)
        do {
            let creds = try await c.pair()
            credentials = creds
            Keychain.save((try? JSONEncoder().encode(creds)) ?? Data(), key: "creds")
            client = c
            await refresh()
            pairing = false
            step = .lights
        } catch is CancellationError { pairing = false }
        catch { pairing = false; self.error = error.localizedDescription }
    }

    func forgetBridge() async {
        if demo { await setDemo(false); return }
        if syncing { await stop() }
        Keychain.delete("creds"); credentials = nil; bridge = nil; Store.set("bridge", nil as BridgeInfo?)
        lights = []; groups = []; scenes = []; areas = []; onboarded = false; step = .welcome
        UserDefaults.standard.set(false, forKey: "onboarded")
    }

    func finishOnboarding() { onboarded = true; UserDefaults.standard.set(true, forKey: "onboarded") }

    // MARK: Hue data

    func refresh() async {
        if demo { return }
        guard let client else { return }
        async let l = try? client.lights()
        async let g = try? client.groups()
        async let s = try? client.scenes()
        async let a = try? client.areas()
        let (ll, gg, ss, aa) = await (l, g, s, a)
        if let ll { lights = ll }; if let gg { groups = gg }; if let ss { scenes = ss }; if let aa { areas = aa }
        if ll == nil && gg == nil { error = HueError.unreachable("no answer").localizedDescription }
        if profile.areaID == nil || !areas.contains(where: { $0.id == profile.areaID }) {
            profile.areaID = (areas.first { $0.type == "screen" } ?? areas.first)?.id
        }
        if let a = selectedArea {
            var p = profile; p.ensureRoutes(for: a.channels)
            if p.routes != profile.routes { profile = p }
        }
        diagnostics = await client.diagnostics()
    }

    func requestScreenAccess() { _ = CGRequestScreenCaptureAccess() }

    /// Screen and system audio share one macOS permission. macOS only applies it to a fresh process,
    /// so once it is switched on the app reopens itself instead of asking the user to.
    private var accessWatch: Task<Void, Never>?
    func watchScreenAccess() {
        guard accessWatch == nil else { return }
        accessWatch = Task { @MainActor in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(1))
                if CGPreflightScreenCaptureAccess() { relaunch(); return }
            }
        }
    }
    private func relaunch() {
        let cfg = NSWorkspace.OpenConfiguration(); cfg.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: cfg) { _, _ in
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }
    }

    // MARK: sync

    func toggleSync() async { syncing ? await stop() : await start() }

    func start() async {
        error = nil; notice = nil
        guard let b = bridge, let creds = credentials else { error = "Set up your bridge first."; return }
        guard let area = selectedArea else { error = "Pick or create a sync area under Sync Lights."; return }
        refreshDisplays()
        if !profile.windowSources.isEmpty { await refreshWindows() }
        let (plan, targets, audioOnly) = buildPlan()
        if !demo && !plan.physicalSources.isEmpty && !screenAllowed {
            requestScreenAccess()
            notice = "Switch on \(Brand.name) under System Settings > Privacy & Security > Screen & System Audio Recording. The app reopens by itself once you do."
            watchScreenAccess()
            return
        }
        if plan.physicalSources.isEmpty && audioOnly.isEmpty && profile.mode == .extend {
            error = "No light is assigned to a display yet. Drag lights onto a display under Sync Source."
            return
        }
        activeSubstitutions = []
        announce(plan)
        do {
            try await engine.start(SyncEngine.Config(bridge: b, creds: creds, area: area, lights: lights, settings: profile.settings, demo: demo))
            syncing = true
            await engine.apply(plan: plan, targets: targets, audioOnlyChannels: audioOnly)
            await engine.applyAudio(profile.settings.audio)
            startPing()
        } catch {
            await engine.stop()
            syncing = false
            self.error = error.localizedDescription
        }
    }

    func stop() async {
        pingTask?.cancel()
        await engine.stop()
        syncing = false; metrics = Metrics(); preview = [:]
    }

    private func startPing() {
        pingTask?.cancel()
        pingTask = Task { [weak self] in
            while !Task.isCancelled {
                if let c = self?.client, let r = try? await c.config() { self?.networkMs = r.latencyMs }
                try? await Task.sleep(nanoseconds: 5_000_000_000)
            }
        }
    }

    // MARK: lights, scenes, rooms, zones

    private func act(_ work: @escaping (HueClient) async throws -> Void) {
        guard let client else { return }
        Task { do { try await work(client) } catch { self.error = error.localizedDescription } }
    }

    func setLight(_ l: HueLight, on: Bool? = nil, brightness: Double? = nil, rgb: RGB? = nil) {
        if let i = lights.firstIndex(of: l) {
            if let on { lights[i].isOn = on }
            if let brightness { lights[i].brightness = brightness; lights[i].isOn = true }
        }
        act { try await $0.setLight(l.id, on: on ?? (brightness != nil || rgb != nil ? true : nil), brightness: brightness, rgb: rgb, gamut: l.gamut) }
    }

    func flash(_ l: HueLight) {
        act { c in
            try await c.setLight(l.id, on: true, brightness: 100, rgb: RGB(1, 1, 1), gamut: l.gamut, durationMs: 0)
            try await Task.sleep(nanoseconds: 400_000_000)
            try await c.setLight(l.id, on: l.isOn, brightness: l.brightness, durationMs: 200)
        }
    }

    func setGroup(_ g: HueGroup, on: Bool? = nil, brightness: Double? = nil) {
        guard let gl = g.groupedLightID else { return }
        for id in g.lightIDs { if let i = lights.firstIndex(where: { $0.id == id }) {
            if let on { lights[i].isOn = on }
            if let brightness { lights[i].brightness = brightness }
        } }
        act { try await $0.setGroup(gl, on: on, brightness: brightness) }
    }

    func isOn(_ g: HueGroup) -> Bool { lights.contains { g.lightIDs.contains($0.id) && $0.isOn } }
    func brightness(_ g: HueGroup) -> Double {
        let ls = lights.filter { g.lightIDs.contains($0.id) && $0.isOn }
        return ls.isEmpty ? 100 : ls.map(\.brightness).reduce(0, +) / Double(ls.count)
    }

    func recall(_ s: HueScene) {
        act { [weak self] c in
            try await c.recall(scene: s.id)
            try? await Task.sleep(nanoseconds: 600_000_000)
            await self?.refresh()
        }
    }

    func saveZone(id: String?, name: String, lightIDs: [String]) {
        if demo {
            if let id, let i = groups.firstIndex(where: { $0.id == id }) { groups[i].name = name; groups[i].lightIDs = lightIDs }
            else { groups.append(HueGroup(id: "demo-zone-\(UUID().uuidString.prefix(6))", name: name, kind: .zone, lightIDs: lightIDs, groupedLightID: nil)) }
            return
        }
        act { [weak self] c in
            if let id { try await c.updateZone(id, name: name, lightIDs: lightIDs) } else { try await c.createZone(name: name, lightIDs: lightIDs) }
            await self?.refresh()
        }
    }
    func deleteZone(_ g: HueGroup) {
        if demo { groups.removeAll { $0.id == g.id }; return }
        act { [weak self] c in try await c.deleteZone(g.id); await self?.refresh() }
    }

    /// The set of lights that can follow screens. The bridge streams one area at a time, so every light used by any
    /// display must live in this area; which display drives which light is decided by the profile, not by the area.
    func saveArea(id: String?, name: String, lightIDs: [String]) {
        let chosen = lights.filter { lightIDs.contains($0.id) && $0.supportsEntertainment }
        guard !chosen.isEmpty else { error = "Pick at least one light that supports Hue Entertainment."; return }
        if demo {
            let chans = chosen.enumerated().map { EntertainmentChannel(id: $0.offset, x: Double($0.offset) / Double(max(chosen.count - 1, 1)) * 1.6 - 0.8, y: 0.8, z: 0, serviceIDs: [$0.element.entertainmentServiceID ?? ""]) }
            if let id, let i = areas.firstIndex(where: { $0.id == id }) { areas[i].name = name; areas[i].channels = chans }
            else { let a = EntertainmentArea(id: "demo-area-\(UUID().uuidString.prefix(6))", name: name, type: "screen", isActive: false, channels: chans); areas.append(a); profile.areaID = a.id }
            return
        }
        act { [weak self] c in
            if let id { try await c.updateArea(id, lights: chosen) } else { try await c.createArea(name: name, lights: chosen) }
            await self?.refresh()
            if id == nil, let a = self?.areas.first(where: { $0.name == name }) { self?.profile.areaID = a.id }
        }
    }

    func createRecommendedArea() async {
        if demo { return }
        let capable = lights.filter(\.supportsEntertainment)
        guard !capable.isEmpty, let client else {
            error = "None of your lights support Hue Entertainment. Bulbs with the Entertainment logo or Hue Play bars work best."
            return
        }
        busy = true
        do {
            try await client.createArea(name: Brand.areaName, lights: capable)
            await refresh()
            profile.areaID = areas.first { $0.name == Brand.areaName }?.id ?? profile.areaID
        } catch {
            self.error = "\(error.localizedDescription) You can also create an Entertainment area in the Hue app and pick it here."
        }
        busy = false
    }
}
