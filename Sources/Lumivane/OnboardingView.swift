import SwiftUI
import HueCore

struct OnboardingView: View {
    @Environment(AppModel.self) private var model
    @State private var manualIP = ""
    @State private var showManual = false
    @State private var pairTask: Task<Void, Never>?
    @State private var newAreaBusy = false

    var body: some View {
        ZStack {
            Color(nsColor: .windowBackgroundColor).ignoresSafeArea()
            Circle().fill(Brand.warm).frame(width: 520, height: 520).blur(radius: 140).opacity(0.18).offset(x: -380, y: 260)
            Circle().fill(Brand.blue).frame(width: 520, height: 520).blur(radius: 140).opacity(0.18).offset(x: 380, y: -260)
            VStack(spacing: 0) {
                HStack(spacing: 8) {
                    BrandMark().frame(width: 24, height: 24)
                    Text(Brand.name).font(.system(size: 16, weight: .bold, design: .rounded))
                    Spacer()
                    HStack(spacing: 6) {
                        ForEach(SetupStep.allCases, id: \.rawValue) { st in
                            Capsule().fill(st.rawValue <= model.step.rawValue ? AnyShapeStyle(Brand.gradient) : AnyShapeStyle(Color.primary.opacity(0.12)))
                                .frame(width: st == model.step ? 22 : 8, height: 8)
                        }
                    }
                }
                .padding(.horizontal, 36).padding(.top, 30)
                Spacer(minLength: 0)
                ZStack {
                    switch model.step {
                    case .welcome: welcome
                    case .bridge: bridge
                    case .pair: pair
                    case .lights: lights
                    case .area: area
                    case .permission: permission
                    case .done: done
                    }
                }
                .frame(maxWidth: 560)
                .id(model.step)
                .transition(.asymmetric(insertion: .move(edge: .trailing).combined(with: .opacity), removal: .move(edge: .leading).combined(with: .opacity)))
                Spacer(minLength: 0)
            }
            .padding(.bottom, 32)
        }
        .animation(.smooth(duration: 0.45), value: model.step)
    }

    private func hero(_ symbol: String, _ title: String, _ subtitle: String, color: Color = .accentColor) -> some View {
        VStack(spacing: 14) {
            ZStack {
                Circle().fill(color).frame(width: 110, height: 110).blur(radius: 36).opacity(0.45)
                Image(systemName: symbol).font(.system(size: 56, weight: .medium)).foregroundStyle(color).symbolEffect(.pulse)
            }
            Text(title).font(.system(size: 32, weight: .bold, design: .rounded)).multilineTextAlignment(.center)
            Text(subtitle).font(.title3).foregroundStyle(.secondary).multilineTextAlignment(.center)
        }
    }

    private func primary(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Text(title).frame(minWidth: 200) }
            .buttonStyle(GradientButtonStyle())
    }

    private var welcome: some View {
        VStack(spacing: 28) {
            hero("sparkles.tv.fill", "Make your room glow with your screen",
                 "Setup takes about two minutes. No account needed, and nothing leaves your Mac.", color: Brand.warm)
            primary("Get started") { model.step = .bridge }
            Button("Explore with Demo Mode") { Task { await model.setDemo(true) } }
                .buttonStyle(.plain).foregroundStyle(.secondary)
                .help("Virtual lights, rooms and displays. No bridge needed, nothing is saved.")
        }
    }

    private var bridge: some View {
        VStack(spacing: 22) {
            hero("wifi.router.fill", model.searching ? "Looking for your Hue Bridge" : "Choose your Hue Bridge",
                 model.searching ? "Make sure your Mac and bridge are on the same Wi-Fi or network." : "Found on your network.")
            if model.searching { ProgressView().controlSize(.large) }
            ForEach(model.bridges) { b in
                Button {
                    model.select(b); model.step = .pair
                } label: {
                    HStack {
                        Image(systemName: "wifi.router").font(.title2)
                        VStack(alignment: .leading) {
                            Text(b.name ?? "Hue Bridge").font(.headline)
                            Text(b.ip).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer(); Image(systemName: "chevron.right").foregroundStyle(.secondary)
                    }
                    .padding(14).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                }.buttonStyle(.plain)
            }
            if !model.searching && model.bridges.isEmpty {
                Text("No bridge found yet.").foregroundStyle(.secondary)
                primary("Search again") { Task { await model.findBridges() } }
                Text("If macOS asked to allow local network access, choose Allow. You can change it in System Settings > Privacy & Security > Local Network.")
                    .font(.footnote).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Try online lookup") { Task { await model.findBridges(online: true) } }.buttonStyle(.link)
                    .help("Asks Philips' discovery service for your bridge. This shares your network's public IP address with Philips.")
            }
            DisclosureGroup("Enter the address manually", isExpanded: $showManual) {
                HStack {
                    TextField("For example 192.168.1.20", text: $manualIP).textFieldStyle(.roundedBorder)
                        .help("You can find the bridge address in the Hue app under Settings > My Hue system.")
                    Button("Connect") { Task { await model.addManual(ip: manualIP); if model.bridge != nil, model.bridges.count == 1 { model.step = .pair } } }
                }
            }.frame(maxWidth: 380)
        }
        .task { if model.bridges.isEmpty { await model.findBridges(); if model.bridges.count == 1 { model.step = .pair } } }
    }

    private var pair: some View {
        VStack(spacing: 24) {
            hero("button.programmable", "Press the button on your bridge",
                 "Press the large round button on top of the Hue Bridge. \(Brand.name) connects automatically.", color: .blue)
            if model.pairing { ProgressView("Waiting for the button...") }
            else { primary("I pressed it") { startPair() } }
            Button("Back") { model.step = .bridge }.buttonStyle(.link)
        }
        .task { startPair() }
        .onDisappear { pairTask?.cancel() }
    }

    private func startPair() {
        guard !model.pairing else { return }
        pairTask = Task { await model.pair() }
    }

    private var lights: some View {
        VStack(spacing: 18) {
            hero("checkmark.circle.fill", "Connected", "\(model.lights.count) lights found, \(model.lights.filter(\.supportsEntertainment).count) can follow your screen.", color: .green)
            ScrollView {
                VStack(spacing: 6) {
                    ForEach(model.lights) { l in
                        HStack {
                            Image(systemName: l.isOn ? "lightbulb.fill" : "lightbulb").foregroundStyle(l.isOn ? .yellow : .secondary)
                            Text(l.name); Spacer()
                            if l.supportsEntertainment { Label("Screen sync", systemImage: "tv").font(.caption).foregroundStyle(.secondary) }
                        }.padding(.vertical, 4)
                        Divider()
                    }
                }.padding(.horizontal)
            }.frame(maxHeight: 220)
            primary("Continue") { model.step = .area }
        }
    }

    private var area: some View {
        VStack(spacing: 20) {
            hero("tv.and.mediabox", "Choose the lights for your screen",
                 model.areas.isEmpty ? "\(Brand.name) can set this up for you." : "Pick an existing Entertainment area, or let \(Brand.name) create one.")
            ForEach(model.areas) { a in
                Button { model.profile.areaID = a.id } label: {
                    HStack {
                        Image(systemName: model.selectedArea?.id == a.id ? "largecircle.fill.circle" : "circle")
                        Text(a.name).font(.headline); Spacer()
                        Text("\(a.channels.count) lights").foregroundStyle(.secondary)
                    }.padding(12).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
                }.buttonStyle(.plain)
            }
            if model.areas.isEmpty {
                if model.busy { ProgressView() } else { primary("Set up automatically") { Task { await model.createRecommendedArea() } } }
            } else { primary("Continue") { model.step = .permission } }
            Button(model.areas.isEmpty ? "Skip for now" : "Create a new area with all my lights") {
                if model.areas.isEmpty { model.step = .permission } else { Task { await model.createRecommendedArea() } }
            }.buttonStyle(.link)
        }
    }

    private var permission: some View {
        VStack(spacing: 22) {
            hero("rectangle.dashed.badge.record", "Allow screen access",
                 "\(Brand.name) reads colors from your screen to light the room. Nothing is recorded or sent anywhere.")
            if model.screenAllowed { Label("Access granted", systemImage: "checkmark.circle.fill").foregroundStyle(.green).font(.headline) }
            else {
                primary("Allow Screen Recording") { model.requestScreenAccess(); model.watchScreenAccess() }
                Text("After allowing, macOS may ask you to reopen \(Brand.name).").font(.footnote).foregroundStyle(.secondary)
            }
            Button("Continue") { model.step = .done }.buttonStyle(.link)
        }
    }

    private var done: some View {
        VStack(spacing: 26) {
            hero("sparkles", "You're ready", "Start syncing from the main screen or the menu bar icon.", color: .pink)
            primary("Open \(Brand.name)") { model.finishOnboarding(); model.refreshDisplays() }
        }
    }
}
