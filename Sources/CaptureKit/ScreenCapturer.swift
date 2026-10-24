import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import CoreGraphics

public enum CaptureTarget: Hashable, Sendable {
    case display(UInt32)
    case window(UInt32)
}

public enum CaptureError: LocalizedError {
    case targetGone
    public var errorDescription: String? { "The selected screen or window is no longer available." }
}

public func hostSeconds() -> Double { CMTimeGetSeconds(CMClockGetTime(CMClockGetHostTimeClock())) }

/// Captures a display or window downscaled by ScreenCaptureKit to ~256 px wide BGRA frames.
public final class ScreenCapturer: NSObject, SCStreamOutput, SCStreamDelegate, @unchecked Sendable {
    public typealias Handler = @Sendable (CVPixelBuffer, Double) -> Void
    private var stream: SCStream?
    private var handler: Handler?
    private let queue = DispatchQueue(label: "lumivane.capture", qos: .userInteractive)
    public var onStop: (@Sendable (String) -> Void)?

    public func start(target: CaptureTarget, fps: Int, handler: @escaping Handler) async throws {
        let c = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        let filter: SCContentFilter
        let size: CGSize
        switch target {
        case .display(let id):
            guard let d = c.displays.first(where: { $0.displayID == id }) ?? c.displays.first else { throw CaptureError.targetGone }
            filter = SCContentFilter(display: d, excludingWindows: []); size = CGSize(width: d.width, height: d.height)
        case .window(let id):
            guard let w = c.windows.first(where: { $0.windowID == id }) else { throw CaptureError.targetGone }
            filter = SCContentFilter(desktopIndependentWindow: w); size = w.frame.size
        }
        let cfg = SCStreamConfiguration()
        cfg.width = 256
        cfg.height = max(16, Int((256 * size.height / max(size.width, 1)).rounded()))
        cfg.pixelFormat = kCVPixelFormatType_32BGRA
        cfg.colorSpaceName = CGColorSpace.sRGB   // default is the display's space (Display P3 on most Macs), which the color math doesn't expect
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(min(max(fps, 10), 60)))
        cfg.queueDepth = 3
        cfg.showsCursor = false
        cfg.scalesToFit = true
        self.handler = handler
        let s = SCStream(filter: filter, configuration: cfg, delegate: self)
        try s.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        try await s.startCapture()
        stream = s
    }

    public func stop() async {
        handler = nil
        try? await stream?.stopCapture()
        stream = nil
    }

    public func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sb.isValid, let pb = CMSampleBufferGetImageBuffer(sb),
              let atts = CMSampleBufferGetSampleAttachmentsArray(sb, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let raw = atts.first?[.status] as? Int, raw == SCFrameStatus.complete.rawValue else { return }
        // Frame timestamps can sit a few ms ahead of the host clock; a negative delay is recorded as 0.
        let latencyMs = max(0, (hostSeconds() - CMTimeGetSeconds(sb.presentationTimeStamp)) * 1000)
        handler?(pb, latencyMs)
    }

    public func stream(_ stream: SCStream, didStopWithError error: Error) {
        onStop?(error.localizedDescription)
    }
}
