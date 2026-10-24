import SwiftUI
import HueCore

/// The pool of lights that can follow screens, where each light sits on its display, and per-light brightness.
struct SyncSetupView: View {
    @Environment(AppModel.self) private var model
    @State private var editing = false
    @State private var creating = false

    var body: some View {
        @Bindable var m = model
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text("Lights for screen sync").font(.title3.bold()); Spacer()
                Button { creating = true } label: { Label("New area", systemImage: "plus") }
                    .help("Create another set of lights for screen sync")
            }
            if model.areas.isEmpty {
                Card {
                    Text("No sync area yet").font(.headline)
                    Button("Set up automatically") { Task { await model.createRecommendedArea() } }.buttonStyle(.borderedProminent)
                }
            } else if let area = model.selectedArea {
                Card(title: "Area") {
                    HStack {
                        Picker("", selection: Binding(get: { area.id }, set: { model.profile.areaID = $0 })) {
                            ForEach(model.areas) { Text($0.name).tag($0.id) }
                        }.labelsHidden().disabled(model.syncing).frame(maxWidth: 260)
                        Button("Choose lights") { editing = true }.disabled(model.syncing)
                            .help("Add or remove lights that can follow your screens")
                    }
                    Text("Drag a light to tell \(Brand.name) which part of its display it should follow.").font(.callout).foregroundStyle(.secondary)
                    ScreenLayoutEditor(area: area).frame(height: 320)
                }
                Card(title: "Per-light brightness") {
                    ForEach(area.channels) { ch in
                        HStack {
                            Text(model.lightName(ch)).frame(width: 180, alignment: .leading).lineLimit(1)
                            Slider(value: Binding(get: { model.profile.settings.channelGain[ch.id] ?? 1 }, set: { model.profile.settings.channelGain[ch.id] = $0 }), in: 0.1...1)
                            Button { if let l = model.light(for: ch) { model.flash(l) } } label: { Image(systemName: "sparkle") }.help("Flash this light")
                        }
                    }
                }
            }
        }
        .sheet(isPresented: $creating) {
            LightPicker(title: "New sync area", initialName: "Living room", initialIDs: [], lights: model.lights, showName: true, onlyEntertainment: true) {
                model.saveArea(id: nil, name: $0, lightIDs: $1) }
        }
        .sheet(isPresented: $editing) {
            if let a = model.selectedArea {
                LightPicker(title: "Lights in \(a.name)", initialName: a.name,
                            initialIDs: Set(model.lights.filter { l in a.channels.contains { $0.serviceIDs.contains(l.entertainmentServiceID ?? "-") } }.map(\.id)),
                            lights: model.lights, showName: false, onlyEntertainment: true) { _, ids in model.saveArea(id: a.id, name: a.name, lightIDs: ids) }
            }
        }
    }
}

struct ScreenLayoutEditor: View {
    @Environment(AppModel.self) private var model
    let area: EntertainmentArea
    @State private var editSource: String = ""

    private var sourcesInUse: [String] {
        Array(Set(model.profile.routes.compactMap { r in r.enabled ? r.sourceID : nil })).sorted()
    }
    private var visible: [LightRoute] {
        let ids = Set(area.channels.map(\.id))
        let rs = model.profile.routes.filter { ids.contains($0.channelID) && $0.enabled }
        return model.profile.mode == .extend ? rs.filter { $0.sourceID == editSource } : rs
    }

    var body: some View {
        VStack(spacing: 8) {
            if model.profile.mode == .extend {
                HStack {
                    Text("Positioning lights on")
                    Picker("", selection: Binding(get: { editSource.isEmpty ? (sourcesInUse.first ?? "") : editSource }, set: { editSource = $0 })) {
                        ForEach(sourcesInUse, id: \.self) { Text(model.sourceName($0)).tag($0) }
                    }.labelsHidden().frame(maxWidth: 280)
                    Spacer()
                }
                .onAppear { if editSource.isEmpty { editSource = sourcesInUse.first ?? "" } }
            }
            GeometryReader { geo in
                let w = min(geo.size.width * 0.85, geo.size.height * 0.85 * 16 / 9), h = w * 9 / 16
                let origin = CGPoint(x: (geo.size.width - w) / 2, y: (geo.size.height - h) / 2)
                ZStack(alignment: .topLeading) {
                    RoundedRectangle(cornerRadius: 10).strokeBorder(.secondary, lineWidth: 3).frame(width: w, height: h)
                        .background(RoundedRectangle(cornerRadius: 10).fill(.black.opacity(0.25)))
                        .offset(x: origin.x, y: origin.y)
                    ForEach(visible, id: \.channelID) { r in
                        let c = model.preview[r.channelID].map { Color($0) } ?? .accentColor
                        Circle().fill(c).frame(width: 30, height: 30)
                            .overlay(Text("\(r.channelID + 1)").font(.caption.bold()).foregroundStyle(.white))
                            .shadow(color: c.opacity(0.8), radius: 8)
                            .position(x: origin.x + r.u * w, y: origin.y + r.v * h)
                            .gesture(DragGesture().onChanged { v in
                                let u = min(max((v.location.x - origin.x) / w, 0), 1), vv = min(max((v.location.y - origin.y) / h, 0), 1)
                                model.updateRoute(r.channelID) { $0.u = u; $0.v = vv }
                            })
                            .help("Drag to move this light around the screen")
                    }
                }
            }
        }
    }
}
