import SwiftUI
import HueCore

struct LevelBar: View {
    let value: Float
    var tint: Color = .accentColor
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(.secondary.opacity(0.2))
                Capsule().fill(tint.gradient).frame(width: max(4, g.size.width * CGFloat(min(max(value, 0), 1))))
                    .animation(.linear(duration: 0.08), value: value)
            }
        }.frame(height: 10)
    }
}

private enum InputChoice: Hashable { case system, app, microphone }

struct AudioView: View {
    @Environment(AppModel.self) private var model
    @State private var appID = ""

    private var choice: InputChoice {
        switch model.profile.settings.audio.input { case .system: .system; case .app: .app; case .microphone: .microphone }
    }

    var body: some View {
        @Bindable var m = model
        let f = model.metrics.audio
        VStack(alignment: .leading, spacing: 16) {
            PageHeader("Sound", subtitle: "Let music and effects move the light")
            NoticeBanner()
            Card {
                Toggle("Lights react to sound", isOn: $m.profile.settings.audio.enabled)
                    .font(.headline).help("Turn off to follow only the picture")
                Text("\(Brand.name) reads the digital audio your Mac is playing, before it reaches the speakers. Room noise, voices and speaker echo don't affect it, and it works with headphones, HDMI and AirPlay.")
                    .font(.callout).foregroundStyle(.secondary)
            }
            Card(title: "Audio source") {
                Picker("", selection: Binding(get: { choice }, set: { c in
                    switch c {
                    case .system: model.profile.settings.audio.input = .system
                    case .app: if let a = model.audioApps.first(where: { $0.bundleID == appID }) ?? model.audioApps.first { appID = a.bundleID; model.profile.settings.audio.input = .app(a.bundleID) }
                    case .microphone: model.profile.settings.audio.input = .microphone
                    }
                })) {
                    Text("Everything my Mac plays (recommended)").tag(InputChoice.system)
                    Text("One app only").tag(InputChoice.app)
                    Text("Microphone (fallback)").tag(InputChoice.microphone)
                }.pickerStyle(.radioGroup).labelsHidden()
                if choice == .app {
                    Picker("App", selection: Binding(get: { if case .app(let b) = model.profile.settings.audio.input { return b } else { return "" } },
                                                     set: { appID = $0; model.profile.settings.audio.input = .app($0) })) {
                        ForEach(model.audioApps, id: \.bundleID) { Text($0.name).tag($0.bundleID) }
                    }.frame(maxWidth: 360).help("Only this app's sound drives the lights")
                }
                if choice == .microphone {
                    Text("Listens to the room, so it also hears voices and speaker echo. Use it only if system audio isn't available.")
                        .font(.footnote).foregroundStyle(.orange)
                }
            }
            Card(title: "How much sound shapes the light") {
                SliderRow(title: "Audio influence", value: $m.profile.settings.audio.influence, range: 0...1, help: "0 follows only the picture. Movies work well at 10-30%, music at 70-90%.")
                SliderRow(title: "Bass sensitivity", value: $m.profile.settings.audio.bassSensitivity, range: 0...2, help: "Warm room-wide pulses on kicks and explosions")
                SliderRow(title: "Treble sensitivity", value: $m.profile.settings.audio.trebleSensitivity, range: 0...2, help: "Quick sparkles on hi-hats and sharp sounds")
                SliderRow(title: "Beat intensity", value: $m.profile.settings.audio.beatIntensity, range: 0...2, help: "Strength of the pulse on each beat")
                SliderRow(title: "Smoothing", value: $m.profile.settings.audio.smoothing, range: 0...1, help: "Higher makes the lights relax more slowly after a hit")
                SliderRow(title: "Music mode strength", value: $m.profile.settings.audio.musicStrength, range: 0...1, help: "How much more the lights react when music is playing than when people talk")
            }
            Card(title: "Live") {
                HStack { Text("Bass").frame(width: 70, alignment: .leading); LevelBar(value: f.bass, tint: .orange) }
                HStack { Text("Mid").frame(width: 70, alignment: .leading); LevelBar(value: f.mid, tint: .green) }
                HStack { Text("Treble").frame(width: 70, alignment: .leading); LevelBar(value: f.treble, tint: .cyan) }
                HStack(spacing: 18) {
                    Label(f.beat > 0.3 ? "Beat" : "  ", systemImage: "circle.fill").foregroundStyle(f.beat > 0.3 ? .pink : .clear)
                    Text(f.tempo > 0 ? String(format: "%.0f BPM", f.tempo) : "Tempo unknown").monospacedDigit()
                    Text(f.speech > 0.5 ? "Speech" : "Music").padding(.horizontal, 8).padding(.vertical, 2)
                        .background(.thinMaterial, in: Capsule()).help("Estimated from the sound itself. Speech gets calmer lighting.")
                    Text(String(format: "%.0f dB", max(f.volumeDB, -99))).monospacedDigit().foregroundStyle(.secondary)
                    Spacer()
                    if model.syncing && model.metrics.audioActive { Text(String(format: "Sound to bridge %.0f ms", model.metrics.audioToLightMs)).font(.callout).foregroundStyle(.secondary) }
                }
                if !model.syncing { Text("Start syncing to see live levels.").font(.footnote).foregroundStyle(.secondary) }
            }
        }
        .task { await model.refreshAudioApps() }
    }
}

struct ProfilesView: View {
    @Environment(AppModel.self) private var model
    @State private var newName = ""

    private func summary(_ p: Profile) -> String {
        let src: String
        switch p.mode {
        case .single: src = p.primarySource.map { model.sourceName($0) } ?? "Built-in display"
        case .mirror: src = p.mirrorSources.isEmpty ? "Mirror" : "Mirror of " + p.mirrorSources.map { model.sourceName($0) }.joined(separator: ", ")
        case .extend: src = "Extend across \(Set(p.routes.compactMap { $0.enabled ? $0.sourceID : nil }).count) displays"
        }
        let a = p.settings.audio
        return "\(src)  ·  \(p.routes.filter(\.enabled).count) lights  ·  sound \(a.enabled ? "\(Int(a.influence * 100))%" : "off")"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            PageHeader("Profiles", subtitle: "Save a whole setup and switch in one click")
            NoticeBanner()
            Text("A profile remembers the display, which lights follow it, brightness and all sync and sound settings. Loading one restores them together.")
                .font(.callout).foregroundStyle(.secondary)
            ForEach(model.profiles) { p in
                let active = p.id == model.profile.id
                Card {
                    HStack(spacing: 14) {
                        Image(systemName: p.symbol).font(.title).frame(width: 36).foregroundStyle(active ? Color.accentColor : .secondary)
                        VStack(alignment: .leading, spacing: 3) {
                            TextField("Name", text: Binding(get: { p.name }, set: { model.rename(p, to: $0) })).textFieldStyle(.plain).font(.headline)
                            Text(summary(p)).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        if active { Label("Active", systemImage: "checkmark.circle.fill").foregroundStyle(.green) }
                        else { Button("Use") { model.load(p) }.buttonStyle(.borderedProminent) }
                        Button(role: .destructive) { model.delete(p) } label: { Image(systemName: "trash") }
                            .disabled(model.profiles.count < 2).help("Delete this profile")
                    }
                }
                .overlay(RoundedRectangle(cornerRadius: 16).stroke(active ? Color.accentColor : .clear, lineWidth: 2))
            }
            Card(title: "Save the current setup as a new profile") {
                HStack {
                    TextField("For example Movie night on the LG", text: $newName).textFieldStyle(.roundedBorder)
                    Button("Save") { model.saveCurrentAsNew(named: newName); newName = "" }.disabled(newName.isEmpty)
                }
            }
        }
        // The first text field would otherwise take focus and show its name selected.
        .onAppear { DispatchQueue.main.async { NSApp.keyWindow?.makeFirstResponder(nil) } }
    }
}
