import SwiftUI
import AppKit
import HueCore
import CaptureKit

enum NavSection: String, CaseIterable, Identifiable {
    case home = "Home", screens = "Screens", sound = "Sound", lights = "Lights", profiles = "Profiles", settings = "Settings"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .home: "sparkles.tv.fill"
        case .screens: "display.2"
        case .sound: "waveform"
        case .lights: "lightbulb.2.fill"
        case .profiles: "rectangle.stack.fill"
        case .settings: "gearshape.fill"
        }
    }
}

struct MainView: View {
    @Environment(AppModel.self) private var model
    @State private var selection: NavSection? = LaunchOptions.section ?? .home
    private var section: NavSection { selection ?? .home }

    var body: some View {
        NavigationSplitView {
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    BrandMark().frame(width: 30, height: 30)
                    Text(Brand.name).font(.system(size: 19, weight: .bold, design: .rounded))
                    Spacer()
                }.padding(.horizontal, 16).padding(.top, 14).padding(.bottom, 6)
                List(NavSection.allCases, selection: $selection) { s in
                    Label(s.rawValue, systemImage: s.icon).tag(s)   // the selection is a NavSection, so each row needs this tag; without it the rows' implicit tag is their String id and no click matches
                }
                .listStyle(.sidebar)
                SidebarStatus()
            }
            .navigationSplitViewColumnWidth(min: 200, ideal: 216)
        } detail: {
            ZStack {
                AmbientBackdrop()
                ScrollView {
                    Group {
                        switch section {
                        case .home: HomeView()
                        case .screens: ScreensView()
                        case .sound: AudioView()
                        case .lights: LightsHubView()
                        case .profiles: ProfilesView()
                        case .settings: SettingsView()
                        }
                    }
                    .padding(28).frame(maxWidth: 960)
                    .id(section)
                    .transition(.asymmetric(insertion: .opacity.combined(with: .offset(y: 10)), removal: .opacity))
                }
                .frame(maxWidth: .infinity)
            }
            .animation(.smooth(duration: 0.28), value: section)
        }
        .toolbar {
            ToolbarItem {
                Button { Task { await model.refresh() } } label: { Label("Refresh", systemImage: "arrow.clockwise") }
                    .help("Reload lights, scenes and rooms from the bridge")
            }
        }
    }
}

struct SidebarStatus: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        HStack(spacing: 10) {
            Circle().fill(model.syncing ? Color.green : Color.secondary.opacity(0.5)).frame(width: 9, height: 9)
                .shadow(color: model.syncing ? .green.opacity(0.8) : .clear, radius: 5)
            VStack(alignment: .leading, spacing: 1) {
                Text(model.syncing ? "Syncing" : "Not syncing").font(.callout.weight(.medium))
                Text(model.profile.name).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
        }
        .padding(14)
        .animation(.smooth, value: model.syncing)
    }
}

struct ProfileMenu: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        Menu {
            ForEach(model.profiles) { p in
                Button { model.load(p) } label: { Label(p.name, systemImage: p.id == model.profile.id ? "checkmark" : p.symbol) }
            }
        } label: { Label(model.profile.name, systemImage: model.profile.symbol) }
            .help("Switch profile: display, lights and sync settings load together")
    }
}

// MARK: - Home

struct HomeView: View {
    @Environment(AppModel.self) private var model

    private var pills: [(String, String, Tone)] {
        var p: [(String, String, Tone)] = []
        p.append(model.diagnostics?.reachable == true ? ("Bridge connected", "wifi.router.fill", .good) : ("Bridge not reachable", "wifi.router.fill", .warn))
        let n = model.selectedArea?.channels.count ?? 0
        p.append(("\(n) light\(n == 1 ? "" : "s") in sync area", "lightbulb.2.fill", n > 0 ? .good : .warn))
        p.append(model.screenAllowed ? ("Screen access", "checkmark.shield.fill", .good) : ("Screen access needed", "exclamationmark.shield.fill", .warn))
        let a = model.profile.settings.audio
        if a.enabled && a.influence > 0 { p.append((model.syncing && !model.metrics.audioActive ? "Sound starting" : "Sound on", "waveform", .good)) }
        else { p.append(("Sound off", "waveform.slash", .off)) }
        if model.syncing { p.append(model.metrics.mode.hasPrefix("Entertainment") ? ("Smooth streaming", "bolt.fill", .good) : ("Basic mode", "tortoise.fill", .warn)) }
        return p
    }

    var body: some View {
        @Bindable var m = model
        VStack(spacing: 20) {
            NoticeBanner()
            Card {
                VStack(spacing: 18) {
                    RoomPreview().frame(maxHeight: 300)
                    HStack(spacing: 8) {
                        ForEach(Array(pills.enumerated()), id: \.offset) { _, x in StatusPill(text: x.0, symbol: x.1, tone: x.2) }
                        Spacer(minLength: 0)
                    }
                    HStack(spacing: 16) {
                        ProfileMenu()
                        Spacer()
                        Button(model.syncing ? "Stop" : "Start syncing") { Task { await model.toggleSync() } }
                            .buttonStyle(GradientButtonStyle(stop: model.syncing))
                            .help(model.syncing ? "Stop syncing your lights" : "Start syncing your lights")
                    }
                }
                .frame(maxWidth: .infinity)
            }
            Card(title: "Color style") {
                HStack(spacing: 12) { ForEach(ColorStyle.allCases) { StyleTile(style: $0) } }
                if !model.syncing { Text("Start syncing to see each style on your current screen.").font(.footnote).foregroundStyle(.secondary) }
            }
            Card(title: "Quick controls") {
                SliderRow(title: "Brightness", value: $m.profile.settings.brightness, range: 0.05...1, help: "Overall light level")
                SliderRow(title: "Sound influence", value: $m.profile.settings.audio.influence, range: 0...1, help: "How much music and sound move the lights. 0 turns it off.")
                Toggle("Night mode", isOn: $m.profile.settings.nightMode).help("Dimmer, warmer light for late evenings")
                DisclosureGroup("Fine tune") {
                    VStack(spacing: 10) {
                        SliderRow(title: "Color intensity", value: $m.profile.settings.intensity, range: 0.5...1.8, help: "Extra color on top of the style")
                        SliderRow(title: "Smoothness", value: $m.profile.settings.smoothing, range: 0...1, help: "Higher is calmer, lower reacts faster")
                        SliderRow(title: "Warmth", value: $m.profile.settings.warmth, range: -1...1, help: "Shifts white balance from cool to warm")
                        SliderRow(title: "Dominant color", value: $m.profile.settings.dominance, range: 0...1, help: "Low follows the average color of an area, high the most prominent color")
                        Toggle("Low-light viewing", isOn: $m.profile.settings.lowLightMode).help("Soft, capped light for dark rooms")
                    }.padding(.top, 8)
                }
            }
        }
        .onAppear { model.setCompare(true) }
        .onDisappear { model.setCompare(false) }
    }
}

struct StyleTile: View {
    @Environment(AppModel.self) private var model
    let style: ColorStyle
    private var selected: Bool { model.profile.settings.style == style }
    private var swatches: [Color] {
        let v = (model.styleCompare[style] ?? [:]).sorted { $0.key < $1.key }.map(\.value)
        guard !v.isEmpty else { return [] }
        let n = min(6, v.count)
        return (0..<n).map { Color(v[$0 * (v.count - 1) / max(n - 1, 1)]) }
    }
    var body: some View {
        Button { model.profile.settings.style = style } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 4) {
                    let s = swatches
                    if s.isEmpty { ForEach(0..<6, id: \.self) { _ in Circle().fill(Color.primary.opacity(0.12)).frame(width: 14, height: 14) } }
                    else { ForEach(Array(s.enumerated()), id: \.offset) { _, c in Circle().fill(c).frame(width: 14, height: 14) } }
                }
                Text(style.title).font(.headline)
                Text(style.summary).font(.caption).foregroundStyle(.secondary).lineLimit(3).multilineTextAlignment(.leading)
                Spacer(minLength: 0)
            }
            .padding(12).frame(maxWidth: .infinity, minHeight: 116, alignment: .topLeading)
            .background(selected ? Brand.pink.opacity(0.10) : Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(selected ? AnyShapeStyle(Brand.gradient) : AnyShapeStyle(Color.clear), lineWidth: 2))
        }
        .buttonStyle(.plain)
        .animation(.smooth(duration: 0.2), value: selected)
        .help(style.summary)
    }
}

// MARK: - Hubs

struct ScreensView: View {
    private enum Tab: String, CaseIterable { case source = "Displays", layout = "Light layout" }
    @State private var tab: Tab = .source
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader("Screens", subtitle: "Choose what the lights follow, and where each light sits")
            Picker("", selection: $tab) { ForEach(Tab.allCases, id: \.self) { Text($0.rawValue).tag($0) } }
                .pickerStyle(.segmented).labelsHidden().frame(maxWidth: 320)
            switch tab {
            case .source: SyncSourceView()
            case .layout: SyncSetupView()
            }
        }
    }
}

struct LightsHubView: View {
    private enum Tab: String, CaseIterable { case lights = "Lights", rooms = "Rooms & zones", scenes = "Scenes" }
    @State private var tab: Tab = .lights
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader("Lights", subtitle: "Control every light, room, zone and scene")
            Picker("", selection: $tab) { ForEach(Tab.allCases, id: \.self) { Text($0.rawValue).tag($0) } }
                .pickerStyle(.segmented).labelsHidden().frame(maxWidth: 420)
            switch tab {
            case .lights: LightsView()
            case .rooms: RoomsView()
            case .scenes: ScenesView()
            }
        }
    }
}

// MARK: - Settings

struct PerformanceView: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        let m = model.metrics
        VStack(alignment: .leading, spacing: 12) {
            if !model.syncing { Text("Start syncing to see live numbers.").foregroundStyle(.secondary) }
            Grid(horizontalSpacing: 12, verticalSpacing: 12) {
                GridRow { stat("Capture rate", String(format: "%.0f fps", m.captureFPS)); stat("Light updates", String(format: "%.0f / s", m.sendHz)) }
                GridRow { stat("Screens captured", "\(m.captureSessions)"); stat("\(Brand.name) CPU", String(format: "%.1f %%", m.cpuPercent)) }
                GridRow { stat("Capture delay", String(format: "%.1f ms", m.captureMs)); stat("Screen to bridge", String(format: "%.1f ms", m.endToEndMs)) }
                GridRow { stat("Color analysis", String(format: "%.2f ms", m.reduceMs)); stat("GPU per frame", String(format: "%.2f ms", m.gpuMs)) }
                GridRow { stat("Sound to bridge", m.audioActive ? String(format: "%.1f ms", m.audioToLightMs) : "off"); stat("Bridge response", model.networkMs.map { String(format: "%.0f ms", $0) } ?? "n/a") }
            }
            Text("Mode: \(m.mode). Times end at the bridge; the radio hop from bridge to bulb is outside the app's control.").font(.footnote).foregroundStyle(.secondary)
        }
    }
    private func stat(_ t: String, _ v: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(t).font(.caption).foregroundStyle(.secondary)
            Text(v).font(.system(.title3, design: .rounded).bold()).monospacedDigit()
        }
        .padding(12).frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

struct PrivacySummary: View {
    private func row(_ symbol: String, _ title: String, _ text: String, tone: Color = .secondary) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: symbol).foregroundStyle(tone).frame(width: 22)
            VStack(alignment: .leading, spacing: 2) { Text(title).font(.callout.weight(.semibold)); Text(text).font(.callout).foregroundStyle(.secondary) }
        }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("All screen analysis, audio analysis and lighting decisions happen on this Mac.").font(.headline)
            row("cpu", "Processed here", "A small, reduced copy of each frame is averaged into colors and discarded. System audio is turned into levels and beats and discarded. Nothing is recorded or stored.", tone: .green)
            row("wifi.router", "Sent to your Hue Bridge", "Light colors and on/off commands, over your local network only.", tone: .blue)
            row("lock.shield", "Never leaves this Mac", "Screen images, audio, window titles and app names. Your bridge key is kept in the macOS Keychain.", tone: .green)
            row("network", "Optional and off by default", "Online bridge lookup through Philips' discovery service. It reveals your network's public IP address to Philips, so it only runs when you ask for it during setup.", tone: .orange)
            row("hand.raised", "None of this exists", "No analytics, no telemetry, no account, no advertising identifiers.", tone: .green)
        }
    }
}

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @State private var login = false
    @State private var copied = false
    private var version: String { (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "dev" }
    private func copy(_ s: String) {
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s, forType: .string)
        copied = true
        Task { try? await Task.sleep(for: .seconds(2)); copied = false }
    }
    var body: some View {
        @Bindable var m = model
        VStack(alignment: .leading, spacing: 16) {
            PageHeader("Settings")
            Card(title: "General") {
                Toggle("Open \(Brand.name) at login", isOn: $login).help("Starts \(Brand.name) in the menu bar when you sign in")
                    .onChange(of: login) { _, v in model.launchAtLogin = v }
                Picker("Appearance", selection: $m.appearance) { ForEach(Appearance.allCases) { Text($0.rawValue).tag($0) } }
            }
            Card(title: "Light output (saved per profile)") {
                SliderRow(title: "Brightness limit", value: $m.profile.settings.brightnessLimit, range: 0.1...1, help: "Lights never go brighter than this, including sound pulses")
                Toggle("Adapt to bright and dark scenes", isOn: $m.profile.settings.dynamicBrightness)
                    .help("Slowly raises light in dark scenes and lowers it in bright ones")
                SliderRow(title: "Tone curve", value: $m.profile.settings.gamma, range: 0.7...1.6, help: "Below 1 lifts dark tones, above 1 makes them deeper. 1 uses the style as designed.")
                Picker("Update rate", selection: $m.profile.settings.fps) {
                    ForEach([20, 30, 40, 50, 60], id: \.self) { Text("\($0) per second").tag($0) }
                }.help("Applies the next time sync starts")
            }
            Card(title: "Demo Mode") {
                Toggle("Use virtual bridge, lights and displays", isOn: Binding(get: { model.demo }, set: { v in Task { await model.setDemo(v) } }))
                Text(model.demo ? "Demo Mode is on. Nothing is captured, saved or sent to any device. Your real setup is untouched." : "For screenshots, tutorials and trying the interface without a bridge. Your real setup is not changed.")
                    .font(.footnote).foregroundStyle(.secondary)
            }
            Card(title: "Privacy") { PrivacySummary() }
            Card(title: "Hue Bridge") {
                Text(model.diagnostics?.report(redacted: false) ?? "No details yet").font(.system(.callout, design: .monospaced)).textSelection(.enabled)
                HStack {
                    Button("Copy shareable report") { if let d = model.diagnostics { copy(d.report(redacted: true)) } }
                        .help("Hides your IP address, bridge ID and area names")
                    Button("Copy full details") { if let d = model.diagnostics { copy(d.report(redacted: false)) } }
                    if copied { Label("Copied", systemImage: "checkmark").foregroundStyle(.green).transition(.opacity) }
                    Spacer()
                    Button("Pair again or switch bridge") { Task { await model.forgetBridge() } }
                }.animation(.smooth, value: copied)
            }
            Card(title: "Performance") {
                DisclosureGroup("Live numbers") { PerformanceView().padding(.top, 8) }
            }
            Card(title: "About") {
                HStack(spacing: 12) {
                    BrandMark().frame(width: 40, height: 40)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(Brand.name) \(version)").font(.headline)
                        Text(Brand.tagline).font(.callout).foregroundStyle(.secondary)
                    }
                }
                Text("Not affiliated with or endorsed by Signify or Philips Hue. Philips Hue is a trademark of Signify.").font(.footnote).foregroundStyle(.secondary)
            }
        }
        .onAppear { login = model.launchAtLogin }
    }
}

// MARK: - Menu bar

struct MenuBarView: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        @Bindable var m = model
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                BrandMark().frame(width: 22, height: 22)
                Text(Brand.name).font(.headline)
                Spacer()
                Circle().fill(model.syncing ? Color.green : Color.secondary.opacity(0.5)).frame(width: 8, height: 8)
            }
            Button { Task { await model.toggleSync() } } label: {
                Label(model.syncing ? "Stop sync" : "Start sync", systemImage: model.syncing ? "stop.circle.fill" : "play.circle.fill").font(.headline)
            }
            ProfileMenu()
            Picker("Style", selection: $m.profile.settings.style) { ForEach(ColorStyle.allCases) { Text($0.title).tag($0) } }
            Divider()
            SliderRow(title: "Brightness", value: $m.profile.settings.brightness, range: 0.05...1)
            SliderRow(title: "Sound", value: $m.profile.settings.audio.influence, range: 0...1)
            Toggle("Night mode", isOn: $m.profile.settings.nightMode)
            Divider()
            Button("Open \(Brand.name)") { NSApp.activate(ignoringOtherApps: true); NSApp.windows.first?.makeKeyAndOrderFront(nil) }
            Button("Quit") { Task { await model.stop(); NSApp.terminate(nil) } }
        }.padding(14).frame(width: 310)
    }
}
