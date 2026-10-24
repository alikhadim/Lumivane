import SwiftUI
import HueCore

struct LightsView: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if model.lights.isEmpty { Text("No lights found. Pull to refresh with the button in the toolbar.").foregroundStyle(.secondary) }
            ForEach(model.lights) { l in LightRow(light: l) }
        }
    }
}

struct LightRow: View {
    @Environment(AppModel.self) private var model
    let light: HueLight
    @State private var color = Color.white
    @State private var level = 100.0

    var body: some View {
        Card {
            HStack(spacing: 14) {
                Image(systemName: light.isOn ? "lightbulb.fill" : "lightbulb").font(.title2).foregroundStyle(light.isOn ? .yellow : .secondary).frame(width: 30)
                VStack(alignment: .leading) {
                    Text(light.name).font(.headline)
                    if light.supportsEntertainment { Text("Supports screen sync").font(.caption).foregroundStyle(.secondary) }
                }
                Spacer()
                Slider(value: $level, in: 1...100) { editing in if !editing { model.setLight(light, brightness: level) } }
                    .frame(width: 180).help("Brightness")
                ColorPicker("", selection: $color, supportsOpacity: false).labelsHidden().help("Pick a color")
                    .onChange(of: color) { _, c in model.setLight(light, rgb: c.rgb) }
                Button { model.flash(light) } label: { Image(systemName: "sparkle") }.help("Flash this light to identify it")
                Toggle("", isOn: Binding(get: { light.isOn }, set: { model.setLight(light, on: $0) })).labelsHidden().toggleStyle(.switch)
            }
        }
        .onAppear { level = light.brightness }
    }
}

struct ScenesView: View {
    @Environment(AppModel.self) private var model
    private var byGroup: [(name: String, scenes: [HueScene])] {
        let names = Dictionary(uniqueKeysWithValues: model.groups.map { ($0.id, $0.name) })
        return Dictionary(grouping: model.scenes) { names[$0.groupID] ?? "Other" }
            .map { ($0.key, $0.value.sorted { $0.name < $1.name }) }.sorted { $0.0 < $1.0 }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if model.scenes.isEmpty { Text("No scenes found. Create scenes in the Hue app and they appear here.").foregroundStyle(.secondary) }
            ForEach(byGroup, id: \.name) { g in
                VStack(alignment: .leading, spacing: 8) {
                    Text(g.name).font(.headline).foregroundStyle(.secondary)
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 10)], spacing: 10) {
                        ForEach(g.scenes) { s in
                            Button { model.recall(s) } label: {
                                HStack {
                                    Image(systemName: s.isActive ? "checkmark.circle.fill" : "photo").foregroundStyle(s.isActive ? .green : .secondary)
                                    Text(s.name).lineLimit(1)
                                    Spacer(minLength: 0)
                                }.padding(12).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
                            }.buttonStyle(.plain).help("Activate \(s.name) in \(g.name)")
                        }
                    }
                }
            }
        }
    }
}

struct RoomsView: View {
    @Environment(AppModel.self) private var model
    @State private var editing: HueGroup?
    @State private var creating = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Spacer()
                Button { creating = true } label: { Label("New zone", systemImage: "plus") }
                    .help("Group any lights into a zone you can switch and dim together")
            }
            ForEach(model.groups.sorted { ($0.kind.rawValue, $0.name) < ($1.kind.rawValue, $1.name) }) { g in
                GroupRow(group: g, onEdit: { editing = g })
            }
        }
        .sheet(isPresented: $creating) { LightPicker(title: "New zone", initialName: "", initialIDs: [], lights: model.lights, showName: true) { name, ids in
            model.saveZone(id: nil, name: name, lightIDs: ids) } }
        .sheet(item: $editing) { g in
            LightPicker(title: "Edit \(g.name)", initialName: g.name, initialIDs: Set(g.lightIDs), lights: model.lights, showName: true,
                        onDelete: { model.deleteZone(g) }) { name, ids in model.saveZone(id: g.id, name: name, lightIDs: ids) }
        }
    }
}

struct GroupRow: View {
    @Environment(AppModel.self) private var model
    let group: HueGroup
    let onEdit: () -> Void
    @State private var level = 100.0
    var body: some View {
        Card {
            HStack(spacing: 14) {
                Image(systemName: group.kind == .room ? "house.fill" : "square.on.square.dashed").font(.title2).frame(width: 30)
                VStack(alignment: .leading) {
                    Text(group.name).font(.headline)
                    Text("\(group.kind == .room ? "Room" : "Zone"), \(group.lightIDs.count) lights").font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Slider(value: $level, in: 1...100) { e in if !e { model.setGroup(group, brightness: level) } }.frame(width: 180)
                if group.kind == .zone { Button(action: onEdit) { Image(systemName: "pencil") }.help("Choose which lights are in this zone") }
                Toggle("", isOn: Binding(get: { model.isOn(group) }, set: { model.setGroup(group, on: $0) })).labelsHidden().toggleStyle(.switch)
            }
        }.onAppear { level = model.brightness(group) }
    }
}

/// Checklist of lights with an optional name field. Used for zones and sync areas.
struct LightPicker: View {
    @Environment(\.dismiss) private var dismiss
    let title: String
    @State var name: String
    @State var selected: Set<String>
    let lights: [HueLight]
    let showName: Bool
    var onlyEntertainment = false
    var onDelete: (() -> Void)?
    let onSave: (String, [String]) -> Void

    init(title: String, initialName: String, initialIDs: Set<String>, lights: [HueLight], showName: Bool,
         onlyEntertainment: Bool = false, onDelete: (() -> Void)? = nil, onSave: @escaping (String, [String]) -> Void) {
        self.title = title; _name = State(initialValue: initialName); _selected = State(initialValue: initialIDs)
        self.lights = onlyEntertainment ? lights.filter(\.supportsEntertainment) : lights
        self.showName = showName; self.onlyEntertainment = onlyEntertainment; self.onDelete = onDelete; self.onSave = onSave
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).font(.title2.bold())
            if showName { TextField("Name", text: $name).textFieldStyle(.roundedBorder) }
            List(lights) { l in
                Toggle(isOn: Binding(get: { selected.contains(l.id) }, set: { if $0 { selected.insert(l.id) } else { selected.remove(l.id) } })) {
                    Text(l.name)
                }
            }.frame(minHeight: 240)
            HStack {
                if let onDelete { Button("Delete", role: .destructive) { onDelete(); dismiss() } }
                Spacer()
                Button("Cancel") { dismiss() }
                Button("Save") { onSave(name.isEmpty ? "My zone" : name, Array(selected)); dismiss() }
                    .buttonStyle(.borderedProminent).disabled(selected.isEmpty)
            }
        }.padding(20).frame(width: 420)
    }
}
