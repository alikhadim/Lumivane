import SwiftUI
import HueCore
import CaptureKit

/// Chooses which display, desktop or window drives which lights.
struct SyncSourceView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var m = model
        VStack(alignment: .leading, spacing: 16) {
            NoticeBanner()
            Card(title: "How should your displays drive your lights?") {
                Picker("", selection: Binding(get: { model.profile.mode }, set: { model.setMode($0) })) {
                    Text("One display").tag(SyncMode.single)
                    Text("Mirror").tag(SyncMode.mirror)
                    Text("Extend").tag(SyncMode.extend)
                }.pickerStyle(.segmented).labelsHidden()
                Text(help(model.profile.mode)).font(.callout).foregroundStyle(.secondary)
                if model.profile.mode == .single {
                    HStack {
                        Text("Display")
                        Picker("", selection: Binding(get: { model.effectivePrimary ?? "" }, set: { model.setPrimary($0) })) {
                            ForEach(model.sourceItems.filter(\.connected)) { Text(model.sourceName($0.id)).tag($0.id) }
                        }.labelsHidden().frame(maxWidth: 320).help("Pick the display that drives the selected lights")
                    }
                }
            }
            Card(title: "Displays") {
                ArrangementView().frame(height: 170)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 250), spacing: 14)], spacing: 14) {
                    ForEach(model.sourceItems) { SourceTile(item: $0) }
                }
                HStack {
                    Menu {
                        ForEach(model.windows.filter { w in !model.profile.windowSources.contains { $0.id == w.id } }) { w in
                            Button("\(w.appName): \(w.title)") { model.addWindow(w) }
                        }
                    } label: { Label("Add an app window", systemImage: "macwindow.badge.plus") }
                        .help("Use one app window, for example a video player, as a source")
                    Spacer()
                    Text("Previews update about once a second while this page is open. Only displays that a light follows are captured while syncing.")
                        .font(.footnote).foregroundStyle(.secondary).multilineTextAlignment(.trailing)
                }
            }
            Card(title: "If a display disconnects") {
                HStack {
                    Picker("Use", selection: Binding(get: { model.profile.fallbackSource ?? "" }, set: { model.profile.fallbackSource = $0.isEmpty ? nil : $0 })) {
                        Text("Built-in display (automatic)").tag("")
                        ForEach(model.sourceItems) { Text(model.sourceName($0.id)).tag($0.id) }
                    }.frame(maxWidth: 420)
                }
                Text("Your assignments are kept. When the display returns, \(Brand.name) switches back by itself.").font(.footnote).foregroundStyle(.secondary)
            }
            LightAssignmentCard()
        }
        .animation(.smooth, value: model.notice)
        .task {
            await model.refreshWindows()
            while !Task.isCancelled {
                model.refreshDisplays()
                await model.refreshThumbnails()
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }
    }

    private func help(_ m: SyncMode) -> String {
        switch m {
        case .single: "One display controls all selected lights."
        case .mirror: "Several displays are analysed together. Each light follows the average of the ones you tick."
        case .extend: "Each light follows its own display. Drag lights or whole rooms onto a display below."
        }
    }
}

struct SourceTile: View {
    @Environment(AppModel.self) private var model
    let item: SourceItem

    private var title: String { model.sourceName(item.id) }
    private var detail: String {
        switch item.kind {
        case .display(let d): "\(d.resolution)  ·  \(d.refreshHz.rounded() == 0 ? "" : "\(Int(d.refreshHz.rounded())) Hz")\(d.isUltrawide ? "  ·  Ultrawide" : "")\(d.isBuiltIn ? "  ·  Built-in" : "")"
        case .desktop: "All displays as one canvas"
        case .window(let w): w.appName
        }
    }
    private var assigned: [EntertainmentChannel] {
        guard model.profile.mode == .extend, let a = model.selectedArea else { return [] }
        return a.channels.filter { ch in model.route(ch.id).map { $0.sourceID == item.id && $0.enabled } ?? false }
    }

    var body: some View {
        let selected = model.isSelected(item.id)
        VStack(alignment: .leading, spacing: 8) {
            ZStack {
                Rectangle().fill(.black.opacity(0.35))
                if let t = item.target, let img = model.thumbnails[t] {
                    Image(decorative: img, scale: 1).resizable().scaledToFill()
                } else if case .desktop = item.kind {
                    Image(systemName: "rectangle.3.group").font(.largeTitle).foregroundStyle(.secondary)
                } else {
                    Image(systemName: item.connected ? "display" : "display.trianglebadge.exclamationmark").font(.largeTitle).foregroundStyle(.secondary)
                }
            }
            .aspectRatio(16 / 9, contentMode: .fit).clipShape(RoundedRectangle(cornerRadius: 8))
            .opacity(item.connected ? 1 : 0.5)
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.headline).lineLimit(1)
                    Text(detail).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
                Label(item.connected ? "Connected" : "Disconnected", systemImage: item.connected ? "circle.fill" : "circle")
                    .labelStyle(.titleAndIcon).font(.caption2).foregroundStyle(item.connected ? .green : .orange)
            }
            if !assigned.isEmpty {
                HStack(spacing: 4) {
                    ForEach(assigned) { ch in
                        Label(model.lightName(ch), systemImage: "lightbulb.fill").font(.caption2).padding(.horizontal, 6).padding(.vertical, 2)
                            .background(.yellow.opacity(0.2), in: Capsule())
                    }
                }.lineLimit(1)
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 12).fill(selected ? Color.accentColor.opacity(0.18) : Color.secondary.opacity(0.08)))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(selected ? Color.accentColor : .clear, lineWidth: 2))
        .overlay(alignment: .topTrailing) {
            if selected && model.profile.mode != .extend { Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.accentColor).padding(14) }
        }
        .contentShape(Rectangle())
        .onTapGesture { tap() }
        .dropDestination(for: String.self) { items, _ in items.first.map { model.drop($0, onto: item.id) } ?? false }
        .contextMenu {
            if case .window = item.kind { Button("Remove window", role: .destructive) { model.removeWindow(item.id) } }
        }
        .help(model.profile.mode == .extend ? "Drag lights here to follow this source" : "Click to use this source")
        .animation(.smooth(duration: 0.2), value: selected)
    }

    private func tap() {
        guard item.connected else { return }
        switch model.profile.mode {
        case .single: model.setPrimary(item.id)
        case .mirror: model.toggleMirror(item.id)
        case .extend: break
        }
    }
}

/// The displays laid out as macOS arranges them.
struct ArrangementView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let ds = model.displays.filter(\.isConnected)
        GeometryReader { geo in
            if ds.isEmpty { Text("No display found").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity) }
            else {
                let minX = ds.map(\.x).min()!, maxX = ds.map { $0.x + $0.width }.max()!
                let minY = ds.map(\.y).min()!, maxY = ds.map { $0.y + $0.height }.max()!
                let scale = min(geo.size.width / (maxX - minX), geo.size.height / (maxY - minY)) * 0.96
                let ox = (geo.size.width - (maxX - minX) * scale) / 2, oy = (geo.size.height - (maxY - minY) * scale) / 2
                ZStack {
                    ForEach(Array(ds.enumerated()), id: \.element.id) { i, d in
                        let sel = model.isSelected(d.id)
                        RoundedRectangle(cornerRadius: 6)
                            .fill(sel ? Color.accentColor.opacity(0.3) : Color.secondary.opacity(0.15))
                            .overlay(RoundedRectangle(cornerRadius: 6).stroke(sel ? Color.accentColor : Color.secondary.opacity(0.5), lineWidth: 2))
                            .overlay(VStack(spacing: 2) {
                                Text("\(i + 1)").font(.headline)
                                Text(d.name).font(.caption).lineLimit(1)
                                Text(d.resolution).font(.caption2).foregroundStyle(.secondary)
                            }.padding(4))
                            .frame(width: d.width * scale, height: d.height * scale)
                            .position(x: ox + (d.x - minX + d.width / 2) * scale, y: oy + (d.y - minY + d.height / 2) * scale)
                            .onTapGesture {
                                switch model.profile.mode {
                                case .single: model.setPrimary(d.id)
                                case .mirror: model.toggleMirror(d.id)
                                case .extend: break
                                }
                            }
                            .dropDestination(for: String.self) { items, _ in items.first.map { model.drop($0, onto: d.id) } ?? false }
                    }
                }
            }
        }
    }
}

struct LightAssignmentCard: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Card(title: "Lights") {
            if let area = model.selectedArea {
                if model.profile.mode == .extend {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack {
                            ForEach(model.groups) { g in
                                Label(g.name, systemImage: g.kind == .room ? "house.fill" : "square.on.square.dashed")
                                    .padding(.horizontal, 10).padding(.vertical, 5).background(.thinMaterial, in: Capsule())
                                    .draggable("grp:\(g.id)")
                                    .help("Drag onto a display to send every light in this \(g.kind.rawValue) there")
                            }
                        }
                    }
                }
                ForEach(area.channels) { ch in
                    HStack(spacing: 12) {
                        Toggle("", isOn: Binding(get: { model.route(ch.id)?.enabled ?? true }, set: { v in model.updateRoute(ch.id) { $0.enabled = v } }))
                            .labelsHidden().help("Turn this light's screen sync on or off")
                        Image(systemName: "lightbulb.fill").foregroundStyle(.yellow)
                        Text(model.lightName(ch))
                        Spacer()
                        if model.profile.mode == .extend {
                            Menu {
                                Button("Not assigned") { model.assign(ch.id, to: nil) }
                                Divider()
                                ForEach(model.sourceItems) { s in Button(model.sourceName(s.id)) { model.assign(ch.id, to: s.id) } }
                            } label: {
                                Label(model.route(ch.id)?.sourceID.map { model.sourceName($0) } ?? "Not assigned", systemImage: "display")
                            }.frame(maxWidth: 260)
                        }
                        Button { if let l = model.light(for: ch) { model.flash(l) } } label: { Image(systemName: "sparkle") }.help("Flash this light")
                    }
                    .padding(8).background(.thinMaterial, in: RoundedRectangle(cornerRadius: 10))
                    .draggable("ch:\(ch.id)")
                }
                Text("The bridge streams one Entertainment area at a time, so every light used by any display belongs to the area \"\(area.name)\". Which display drives which light is set here and saved in the profile.")
                    .font(.footnote).foregroundStyle(.secondary)
            } else {
                Text("Set up a sync area first under Sync Lights.").foregroundStyle(.secondary)
            }
        }
    }
}
