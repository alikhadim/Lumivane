import Foundation
import Metal
import CoreVideo
import HueCore

/// GPU zone reduction: one threadgroup per zone accumulates RGB sums and a 12-bin hue histogram.
/// Output layout matches `ZoneAccumulator` (52 floats per zone).
public final class MetalReducer: @unchecked Sendable {
    private let device: MTLDevice
    private let queue: MTLCommandQueue
    private let pipeline: MTLComputePipelineState
    private var cache: CVMetalTextureCache?
    public private(set) var lastGPUms: Double = 0
    private let lock = NSLock()   // capture queues of several displays share this reducer

    public init?() {
        guard let d = MTLCreateSystemDefaultDevice(), let q = d.makeCommandQueue(),
              let lib = try? d.makeLibrary(source: Self.source, options: nil),
              let f = lib.makeFunction(name: "reduceZones"),
              let p = try? d.makeComputePipelineState(function: f) else { return nil }
        device = d; queue = q; pipeline = p
        CVMetalTextureCacheCreate(nil, nil, d, nil, &cache)
        if cache == nil { return nil }
    }

    public func reduce(_ pb: CVPixelBuffer, zones: [ZoneRect]) -> [Float]? {
        guard let cache, !zones.isEmpty, CVPixelBufferGetPixelFormatType(pb) == kCVPixelFormatType_32BGRA else { return nil }
        lock.lock(); defer { lock.unlock() }
        var tex: CVMetalTexture?
        let w = CVPixelBufferGetWidth(pb), h = CVPixelBufferGetHeight(pb)
        guard CVMetalTextureCacheCreateTextureFromImage(nil, cache, pb, nil, .bgra8Unorm, w, h, 0, &tex) == kCVReturnSuccess,
              let t = tex, let mt = CVMetalTextureGetTexture(t) else { return nil }
        let n = zones.count, count = n * ZoneAccumulator.stride
        guard let out = device.makeBuffer(length: count * MemoryLayout<Float>.stride, options: .storageModeShared),
              let cb = queue.makeCommandBuffer(), let enc = cb.makeComputeCommandEncoder() else { return nil }
        let rects = zones.map { SIMD4<Float>($0.x0, $0.y0, $0.x1, $0.y1) }
        enc.setComputePipelineState(pipeline)
        enc.setTexture(mt, index: 0)
        rects.withUnsafeBytes { enc.setBytes($0.baseAddress!, length: $0.count, index: 0) }
        enc.setBuffer(out, offset: 0, index: 1)
        enc.dispatchThreadgroups(MTLSize(width: n, height: 1, depth: 1), threadsPerThreadgroup: MTLSize(width: 64, height: 1, depth: 1))
        enc.endEncoding()
        cb.commit()
        cb.waitUntilCompleted()
        lastGPUms = (cb.gpuEndTime - cb.gpuStartTime) * 1000
        return Array(UnsafeBufferPointer(start: out.contents().bindMemory(to: Float.self, capacity: count), count: count))
    }

    static let source = """
    #include <metal_stdlib>
    using namespace metal;
    #define STRIDE 52
    #define BINS 12
    #define TG 64
    kernel void reduceZones(texture2d<float, access::read> tex [[texture(0)]],
                            constant float4 *zones [[buffer(0)]],
                            device float *out [[buffer(1)]],
                            uint zid [[threadgroup_position_in_grid]],
                            uint tid [[thread_index_in_threadgroup]]) {
        threadgroup float acc[TG * STRIDE];
        float local[STRIDE];
        for (uint i = 0; i < STRIDE; i++) local[i] = 0.0;
        float4 r = zones[zid];
        int W = int(tex.get_width()), H = int(tex.get_height());
        int x0 = min(int(r.x * W), W - 1), y0 = min(int(r.y * H), H - 1);
        int x1 = max(x0 + 1, min(W, int(r.z * W))), y1 = max(y0 + 1, min(H, int(r.w * H)));
        int w = x1 - x0, n = w * (y1 - y0);
        for (int i = int(tid); i < n; i += TG) {
            float3 c = tex.read(uint2(uint(x0 + i % w), uint(y0 + i / w))).rgb;
            local[0] += c.r; local[1] += c.g; local[2] += c.b; local[3] += 1.0;
            float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b)), d = mx - mn;
            float s = mx > 0.0 ? d / mx : 0.0;
            if (mx >= 0.08 && s >= 0.2) {
                float h;
                if (mx == c.r) { h = (c.g - c.b) / d; if (h < 0.0) h += 6.0; }
                else if (mx == c.g) { h = (c.b - c.r) / d + 2.0; }
                else { h = (c.r - c.g) / d + 4.0; }
                h /= 6.0;
                float wt = s * mx;
                uint bin = min(uint(BINS - 1), uint(h * float(BINS)));
                local[4 + bin] += wt;
                local[16 + bin * 3] += c.r * wt; local[17 + bin * 3] += c.g * wt; local[18 + bin * 3] += c.b * wt;
            }
        }
        for (uint i = 0; i < STRIDE; i++) acc[tid * STRIDE + i] = local[i];
        threadgroup_barrier(mem_flags::mem_threadgroup);
        for (uint s = TG / 2; s > 0; s >>= 1) {
            if (tid < s) { for (uint i = 0; i < STRIDE; i++) acc[tid * STRIDE + i] += acc[(tid + s) * STRIDE + i]; }
            threadgroup_barrier(mem_flags::mem_threadgroup);
        }
        if (tid < STRIDE) out[zid * STRIDE + tid] = acc[tid];
    }
    """
}
