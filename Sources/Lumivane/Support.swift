import SwiftUI
import Security
import HueCore

enum Keychain {
    private static func base(_ key: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: Brand.bundleID, kSecAttrAccount as String: key]
    }
    static func save(_ data: Data, key: String) {
        SecItemDelete(base(key) as CFDictionary)
        var a = base(key); a[kSecValueData as String] = data
        SecItemAdd(a as CFDictionary, nil)
    }
    static func load(_ key: String) -> Data? {
        var q = base(key); q[kSecReturnData as String] = true; q[kSecMatchLimit as String] = kSecMatchLimitOne
        var r: AnyObject?
        return SecItemCopyMatching(q as CFDictionary, &r) == errSecSuccess ? r as? Data : nil
    }
    static func delete(_ key: String) { SecItemDelete(base(key) as CFDictionary) }
}

enum Store {
    /// Off while Demo Mode is on, so virtual data never overwrites the real profiles, bridge or displays.
    nonisolated(unsafe) static var persist = true
    static func get<T: Decodable>(_ key: String, as: T.Type = T.self) -> T? {
        UserDefaults.standard.data(forKey: key).flatMap { try? JSONDecoder().decode(T.self, from: $0) }
    }
    static func set<T: Encodable>(_ key: String, _ v: T?) {
        guard persist else { return }
        UserDefaults.standard.set(v.flatMap { try? JSONEncoder().encode($0) }, forKey: key)
    }
}

extension Color {
    init(_ c: RGB) { self.init(.sRGB, red: Double(c.r), green: Double(c.g), blue: Double(c.b)) }
    var rgb: RGB {
        let n = NSColor(self).usingColorSpace(.sRGB) ?? .white
        return RGB(Float(n.redComponent), Float(n.greenComponent), Float(n.blueComponent))
    }
}

enum Appearance: String, CaseIterable, Identifiable {
    case system = "Match system", light = "Light", dark = "Dark"
    var id: String { rawValue }
    var scheme: ColorScheme? { self == .system ? nil : (self == .light ? .light : .dark) }
}

@preconcurrency import UserNotifications

enum Notifier {
    static func post(_ title: String, _ body: String) {
        guard Bundle.main.bundleIdentifier != nil else { return }
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { ok, _ in
            guard ok else { return }
            let content = UNMutableNotificationContent(); content.title = title; content.body = body
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
    }
}
