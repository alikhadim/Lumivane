import SwiftUI

@main
struct LumivaneApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup(Brand.name) {
            RootView()
                .environment(model)
                .frame(minWidth: 960, minHeight: 640)
                .preferredColorScheme(model.appearance.scheme)
                .task { await model.bootOnce() }
        }
        .windowResizability(.contentMinSize)

        MenuBarExtra {
            MenuBarView().environment(model)
        } label: {
            Image(nsImage: MenuIcon.image(active: model.syncing))
        }
        .menuBarExtraStyle(.window)
    }
}

struct RootView: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        Group {
            if model.onboarded && model.hasSetup { MainView().transition(.opacity) } else { OnboardingView().transition(.opacity) }
        }
        .animation(.smooth(duration: 0.45), value: model.onboarded)
        .alert("Something needs attention", isPresented: Binding(get: { model.error != nil }, set: { if !$0 { model.error = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(model.error ?? "") }
    }
}
