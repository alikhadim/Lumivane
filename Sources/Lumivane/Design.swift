import SwiftUI
import AppKit
import HueCore

/// Command-line options, used to produce screenshots and for UI testing: `--demo`, `--section home|screens|sound|lights|profiles|settings`, `--sync`, `--metrics-log <file>`.
enum LaunchOptions {
    static var section: NavSection? {
        let a = CommandLine.arguments
        guard let i = a.firstIndex(of: "--section"), i + 1 < a.count else { return nil }
        return NavSection.allCases.first { $0.rawValue.lowercased() == a[i + 1].lowercased() }
    }
    static var autoSync: Bool { CommandLine.arguments.contains("--sync") }
    /// Developer option for benchmarking: appends one row of live metrics every 0.1 s to this file. Off unless given.
    static var metricsLog: String? {
        let a = CommandLine.arguments
        guard let i = a.firstIndex(of: "--metrics-log"), i + 1 < a.count else { return nil }
        return a[i + 1]
    }
}

/// Product identity in one place. Renaming the app means changing `name` here and in scripts/brand.env.
enum Brand {
    static let name = "Lumivane"
    static let tagline = "Light that follows the picture"
    static let fallbackBundleID = "com.lumivane.app"
    static let areaName = name
    static var bundleID: String { Bundle.main.bundleIdentifier ?? fallbackBundleID }

    static let warm = Color(red: 1.0, green: 0.48, blue: 0.10)
    static let pink = Color(red: 1.0, green: 0.18, blue: 0.48)
    static let blue = Color(red: 0.23, green: 0.48, blue: 1.0)
    static let gradient = LinearGradient(colors: [warm, pink, blue], startPoint: .bottomLeading, endPoint: .topTrailing)
}

/// The K mark, drawn in code so it stays sharp at any size.
struct BrandMark: View {
    var body: some View {
        GeometryReader { g in
            let s = min(g.size.width, g.size.height)
            ZStack {
                Path { p in
                    p.move(to: CGPoint(x: 0.33 * s, y: 0.22 * s)); p.addLine(to: CGPoint(x: 0.33 * s, y: 0.77 * s))
                    p.addLine(to: CGPoint(x: 0.68 * s, y: 0.77 * s))
                }
                .stroke(Brand.gradient, style: StrokeStyle(lineWidth: 0.11 * s, lineCap: .round, lineJoin: .round))
                Circle().fill(Brand.gradient).frame(width: 0.12 * s, height: 0.12 * s).position(x: 0.66 * s, y: 0.30 * s)
            }
        }
        .aspectRatio(1, contentMode: .fit)
    }
}

/// Monochrome template image for the menu bar. A dot marks active sync.
enum MenuIcon {
    static func image(active: Bool) -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            let p = NSBezierPath(); p.lineWidth = 2.2; p.lineCapStyle = .round; p.lineJoinStyle = .round
            p.move(to: NSPoint(x: 5.5, y: 14.5)); p.line(to: NSPoint(x: 5.5, y: 3.5)); p.line(to: NSPoint(x: 12, y: 3.5))
            NSColor.black.setStroke(); p.stroke()
            if active { NSColor.black.setFill(); NSBezierPath(ovalIn: NSRect(x: 11.2, y: 11.2, width: 4, height: 4)).fill() }
            return true
        }
        img.isTemplate = true
        return img
    }
}

// MARK: - Components

struct Card<Content: View>: View {
    var title: String? = nil
    @ViewBuilder var content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let title { Text(title).font(.subheadline.weight(.semibold)).foregroundStyle(.secondary).tracking(0.6).textCase(.uppercase) }
            content
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).strokeBorder(.primary.opacity(0.07), lineWidth: 1))
    }
}

extension PageHeader where Trailing == EmptyView {
    init(_ title: String, subtitle: String? = nil) { self.init(title, subtitle: subtitle) { EmptyView() } }
}

struct PageHeader<Trailing: View>: View {
    let title: String
    var subtitle: String? = nil
    @ViewBuilder var trailing: Trailing
    init(_ title: String, subtitle: String? = nil, @ViewBuilder trailing: () -> Trailing) {
        self.title = title; self.subtitle = subtitle; self.trailing = trailing()
    }
    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.system(size: 30, weight: .bold, design: .rounded))
                if let subtitle { Text(subtitle).font(.callout).foregroundStyle(.secondary) }
            }
            Spacer()
            trailing
        }
    }
}

enum Tone { case good, warn, off
    var color: Color { switch self { case .good: .green; case .warn: .orange; case .off: .secondary } }
}

struct StatusPill: View {
    let text: String
    let symbol: String
    var tone: Tone = .off
    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: symbol).font(.caption.weight(.semibold))
            Text(text).font(.callout.weight(.medium))
        }
        .foregroundStyle(tone.color)
        .padding(.horizontal, 11).padding(.vertical, 6)
        .background(tone.color.opacity(0.12), in: Capsule())
    }
}

struct GradientButtonStyle: ButtonStyle {
    var stop = false
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline).foregroundStyle(.white)
            .padding(.horizontal, 28).padding(.vertical, 12)
            .background(stop ? AnyShapeStyle(Color.red.gradient) : AnyShapeStyle(Brand.gradient), in: Capsule())
            .shadow(color: (stop ? Color.red : Brand.pink).opacity(0.45), radius: configuration.isPressed ? 4 : 14, y: 4)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.smooth(duration: 0.18), value: configuration.isPressed)
    }
}

struct SliderRow: View {
    let title: String
    @Binding var value: Float
    let range: ClosedRange<Float>
    var help = ""
    var body: some View {
        HStack {
            Text(title).frame(width: 150, alignment: .leading)
            Slider(value: $value, in: range)
        }.help(help)
    }
}

struct NoticeBanner: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        if let n = model.notice {
            HStack(spacing: 10) {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                Text(n).font(.callout)
                Spacer()
                Button { withAnimation(.smooth) { model.notice = nil } } label: { Image(systemName: "xmark") }.buttonStyle(.plain)
            }
            .padding(12).background(.orange.opacity(0.14), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .transition(.move(edge: .top).combined(with: .opacity))
        }
    }
}

/// Soft color wash behind the window that follows what the lights are showing.
struct AmbientBackdrop: View {
    @Environment(AppModel.self) private var model
    private var palette: [Color] {
        let v = model.preview.sorted { $0.key < $1.key }.map(\.value)
        guard model.syncing, v.count > 0 else { return [] }
        func avg(_ s: ArraySlice<RGB>) -> Color {
            guard !s.isEmpty else { return .clear }
            let n = Float(s.count)
            return Color(RGB(s.reduce(0) { $0 + $1.r } / n, s.reduce(0) { $0 + $1.g } / n, s.reduce(0) { $0 + $1.b } / n))
        }
        let a = v.count / 3, b = (v.count * 2) / 3
        return [avg(v[0..<max(a, 1)]), avg(v[max(a, 0)..<max(b, 1)]), avg(v[min(b, v.count - 1)..<v.count])]
    }
    var body: some View {
        ZStack {
            Color(nsColor: .windowBackgroundColor)
            let p = palette
            ForEach(Array(p.enumerated()), id: \.offset) { i, c in
                Circle().fill(c).frame(width: 460, height: 460).blur(radius: 120).opacity(0.26)
                    .offset(x: CGFloat(i - 1) * 380, y: i == 1 ? -240 : 220)
            }
        }
        .animation(.easeOut(duration: 0.35), value: palette)
        .ignoresSafeArea()
        .allowsHitTesting(false)
    }
}

/// The screen and its lights, drawn from the saved layout and lit with the live colors.
struct RoomPreview: View {
    @Environment(AppModel.self) private var model

    private var routes: [LightRoute] {
        guard let a = model.selectedArea else { return [] }
        let ids = Set(a.channels.map(\.id))
        return model.profile.routes.filter { $0.enabled && ids.contains($0.channelID) }
    }

    var body: some View {
        GeometryReader { g in
            let w = g.size.width, h = g.size.height
            let sw = w * 0.56, sh = sw * 9 / 16
            ZStack {
                ForEach(routes, id: \.channelID) { r in
                    let c: Color = model.syncing ? Color(model.preview[r.channelID] ?? .black) : Color.secondary.opacity(0.55)
                    ZStack {
                        Circle().fill(c).frame(width: 96, height: 96).blur(radius: 28).opacity(model.syncing ? 0.85 : 0.18)
                        Circle().fill(c).frame(width: 22, height: 22).overlay(Circle().stroke(.white.opacity(0.75), lineWidth: 1.5))
                    }
                    .position(x: w / 2 + (CGFloat(r.u) - 0.5) * sw * 1.5, y: h / 2 + (CGFloat(r.v) - 0.5) * sh * 1.5)
                    .animation(.linear(duration: 0.12), value: model.preview[r.channelID])
                    .onTapGesture { if let ch = model.selectedArea?.channels.first(where: { $0.id == r.channelID }), let l = model.light(for: ch) { model.flash(l) } }
                    .help(model.selectedArea?.channels.first(where: { $0.id == r.channelID }).map { model.lightName($0) + ". Click to flash." } ?? "")
                }
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(LinearGradient(colors: [Color(white: 0.10), Color(white: 0.04)], startPoint: .top, endPoint: .bottom))
                    .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(.white.opacity(0.16), lineWidth: 1.5))
                    .overlay(Text(Brand.name).font(.system(size: 13, weight: .semibold, design: .rounded)).foregroundStyle(.white.opacity(0.18)))
                    .frame(width: sw, height: sh)
                    .allowsHitTesting(false)
            }
        }
        .aspectRatio(16 / 9, contentMode: .fit)
    }
}
