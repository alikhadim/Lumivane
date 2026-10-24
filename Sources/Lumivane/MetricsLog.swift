import Foundation

/// Benchmark recorder, active only when the app is launched with `--metrics-log <file>`. Numbers only: no screen, audio or device data.
@MainActor
final class MetricsLog {
    static let shared: MetricsLog? = LaunchOptions.metricsLog.flatMap { MetricsLog(path: $0) }
    private let handle: FileHandle
    private let start = Date()

    private init?(path: String) {
        let header = "t_s,capture_fps,capture_ms,reduce_ms,gpu_ms,end_to_end_ms,audio_to_light_ms,send_hz,cpu_percent,network_ms,sessions,mode\n"
        guard FileManager.default.createFile(atPath: path, contents: Data(header.utf8)), let h = FileHandle(forWritingAtPath: path) else { return nil }
        _ = try? h.seekToEnd()
        handle = h
    }

    func write(_ m: Metrics, networkMs: Double?) {
        let t = Date().timeIntervalSince(start)
        let net = networkMs.map { String(format: "%.2f", $0) } ?? ""
        let row = String(format: "%.2f,%.1f,%.2f,%.3f,%.3f,%.2f,%.2f,%.1f,%.1f,", t, m.captureFPS, m.captureMs, m.reduceMs, m.gpuMs, m.endToEndMs, m.audioToLightMs, m.sendHz, m.cpuPercent)
            + net + ",\(m.captureSessions),\"\(m.mode)\"\n"
        try? handle.write(contentsOf: Data(row.utf8))
    }
}
