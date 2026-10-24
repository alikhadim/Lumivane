// Re-encodes PNG files without any metadata (no comments, no profile names, no timestamps). Used by screenshots.sh.
import Foundation
import ImageIO
import UniformTypeIdentifiers
for path in CommandLine.arguments.dropFirst() {
    let url = URL(fileURLWithPath: path)
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil), let img = CGImageSourceCreateImageAtIndex(src, 0, nil),
          let dst = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil) else { print("skipped \(path)"); continue }
    CGImageDestinationAddImage(dst, img, nil)
    if !CGImageDestinationFinalize(dst) { print("failed \(path)") }
}
