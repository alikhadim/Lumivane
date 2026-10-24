import Foundation

public struct HueScene: Sendable, Identifiable, Hashable {
    public var id: String
    public var name: String
    public var groupID: String
    public var groupType: String
    public var isActive: Bool
    public init(id: String, name: String, groupID: String, groupType: String, isActive: Bool) {
        self.id = id; self.name = name; self.groupID = groupID; self.groupType = groupType; self.isActive = isActive
    }
}

public struct HueGroup: Sendable, Identifiable, Hashable {
    public enum Kind: String, Sendable { case room, zone }
    public var id: String
    public var name: String
    public var kind: Kind
    public var archetype: String?
    public var lightIDs: [String]
    public var groupedLightID: String?
    public init(id: String, name: String, kind: Kind, archetype: String? = nil, lightIDs: [String], groupedLightID: String?) {
        self.id = id; self.name = name; self.kind = kind; self.archetype = archetype
        self.lightIDs = lightIDs; self.groupedLightID = groupedLightID
    }
}
