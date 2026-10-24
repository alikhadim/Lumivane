import Foundation
import ScreenCaptureKit
import AVFoundation
import CoreMedia
import HueCore

public enum AudioCaptureError: LocalizedError {
    case appNotRunning, noDisplay, micDenied
    public var errorDescription: String? {
        switch self {
        case .appNotRunning: "That app isn't running. Start it, or switch to system audio."
        case .noDisplay: "No display is available for audio capture."
        case .micDenied: "Microphone access was denied. Allow it in System Settings > Privacy & Security > Microphone."
        }
    }
}

/// Direct digital audio: ScreenCaptureKit taps the system mix (or one app) before the output device,
/// so room noise, speakers, headphones, HDMI and AirPlay routing do not affect it.
/// The microphone path exists only as an explicit fallback.
public final class AudioCapturer: NSObject, SCStreamOutput, SCStreamDelegate, @unchecked Sendable {
    /// mono samples, sample rate, host-clock seconds at the end of the buffer
    public typealias Handler = @Sendable ([Float], Double, Double) -> Void
    private var stream: SCStream?
    private var engine: AVAudioEngine?
    private var handler: Handler?
    private let queue = DispatchQueue(label: "lumivane.audio", qos: .userInteractive)
    private let videoQueue = DispatchQueue(label: "lumivane.audio.discard")
    public var onStop: (@Sendable (String) -> Void)?

    public static func runningApps() async -> [(bundleID: String, name: String)] {
        guard let c = try? await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: false) else { return [] }
        let me = Bundle.main.bundleIdentifier
        return c.applications.filter { !$0.applicationName.isEmpty && $0.bundleIdentifier != me }
            .map { ($0.bundleIdentifier, $0.applicationName) }.sorted { $0.1.localizedCaseInsensitiveCompare($1.1) == .orderedAscending }
    }

    public func start(_ input: AudioInput, handler: @escaping Handler) async throws {
        await stop()
        self.handler = handler
        if input == .microphone { try await startMicrophone(); return }

        let c = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: false)
        guard let d = c.displays.first else { throw AudioCaptureError.noDisplay }
        let filter: SCContentFilter
        if case .app(let bundle) = input {
            guard let app = c.applications.first(where: { $0.bundleIdentifier == bundle }) else { throw AudioCaptureError.appNotRunning }
            filter = SCContentFilter(display: d, including: [app], exceptingWindows: [])
        } else {
            filter = SCContentFilter(display: d, excludingApplications: [], exceptingWindows: [])
        }
        let cfg = SCStreamConfiguration()
        cfg.capturesAudio = true
        cfg.excludesCurrentProcessAudio = true
        cfg.sampleRate = 48000
        cfg.channelCount = 2
        // A stream always carries video; keep it as small and slow as possible (2x2 px, 1 fps) and discard it.
        cfg.width = 2; cfg.height = 2
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        cfg.queueDepth = 3
        let s = SCStream(filter: filter, configuration: cfg, delegate: self)
        try s.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        try s.addStreamOutput(self, type: .screen, sampleHandlerQueue: videoQueue)
        try await s.startCapture()
        stream = s
    }

    public func stop() async {
        handler = nil
        if let e = engine { e.inputNode.removeTap(onBus: 0); e.stop(); engine = nil }
        try? await stream?.stopCapture()
        stream = nil
    }

    private func startMicrophone() async throws {
        guard await AVCaptureDevice.requestAccess(for: .audio) else { throw AudioCaptureError.micDenied }
        let e = AVAudioEngine()
        let node = e.inputNode
        let fmt = node.outputFormat(forBus: 0)
        node.installTap(onBus: 0, bufferSize: 512, format: fmt) { [weak self] buf, _ in
            guard let ch = buf.floatChannelData else { return }
            let n = Int(buf.frameLength), c = Int(buf.format.channelCount)
            var mono = [Float](repeating: 0, count: n)
            for i in 0..<n { var s: Float = 0; for k in 0..<c { s += ch[k][i] }; mono[i] = s / Float(c) }
            self?.handler?(mono, buf.format.sampleRate, hostSeconds())
        }
        try e.start()
        engine = e
    }

    public func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .audio, sb.isValid, let handler,
              let desc = CMSampleBufferGetFormatDescription(sb),
              let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(desc)?.pointee else { return }
        var size = 0
        CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sb, bufferListSizeNeededOut: &size, bufferListOut: nil, bufferListSize: 0,
                                                                blockBufferAllocator: nil, blockBufferMemoryAllocator: nil, flags: 0, blockBufferOut: nil)
        guard size > 0 else { return }
        let raw = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
        defer { raw.deallocate() }
        let abl = raw.bindMemory(to: AudioBufferList.self, capacity: 1)
        var block: CMBlockBuffer?
        let st = CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sb, bufferListSizeNeededOut: nil, bufferListOut: abl, bufferListSize: size,
                                                                         blockBufferAllocator: nil, blockBufferMemoryAllocator: nil,
                                                                         flags: kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment, blockBufferOut: &block)
        guard st == noErr else { return }
        let list = UnsafeMutableAudioBufferListPointer(abl)
        let frames = CMSampleBufferGetNumSamples(sb)
        guard frames > 0 else { return }
        var mono = [Float](repeating: 0, count: frames)
        let nonInterleaved = asbd.mFormatFlags & kAudioFormatFlagIsNonInterleaved != 0
        if nonInterleaved {
            let chans = max(list.count, 1)
            for b in list { if let p = b.mData?.assumingMemoryBound(to: Float.self) { for i in 0..<min(frames, Int(b.mDataByteSize) / 4) { mono[i] += p[i] } } }
            let k = 1 / Float(chans); for i in 0..<frames { mono[i] *= k }
        } else if let b = list.first, let p = b.mData?.assumingMemoryBound(to: Float.self) {
            let chans = max(Int(asbd.mChannelsPerFrame), 1)
            for i in 0..<frames { var s: Float = 0; for c in 0..<chans { s += p[i * chans + c] }; mono[i] = s / Float(chans) }
        }
        let end = CMTimeGetSeconds(sb.presentationTimeStamp) + Double(frames) / asbd.mSampleRate
        handler(mono, asbd.mSampleRate, end)
    }

    public func stream(_ stream: SCStream, didStopWithError error: Error) { onStop?(error.localizedDescription) }
}
