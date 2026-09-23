import SwiftUI

struct MainView: View {
  @Bindable var model: Presentation
  var body: some View {
    NavigationSplitView {
      VStack(alignment: .leading, spacing: 16) {
        HStack(spacing: 9) {
          MeterMark()
          Text("AgentMeter").font(.headline)
        }.padding(.horizontal, 15).padding(.top, 15)
        List(Destination.allCases, selection: $model.destination) { destination in
          Label(destination.title, systemImage: destination.symbol).tag(destination)
        }.listStyle(.sidebar)
      }.navigationSplitViewColumnWidth(min: 150, ideal: 166, max: 190)
    } detail: {
      Group {
        switch model.destination ?? .usage {
        case .usage: UsageView(model: model)
        case .settings: SettingsView(preferences: model.preferences, login: model.loginItem)
        case .about: AboutView()
        }
      }.frame(minWidth: 410, minHeight: 420).background(Color(nsColor: .windowBackgroundColor))
    }.navigationSplitViewStyle(.balanced)
  }
}
struct SettingsView: View {
  @Bindable var preferences: Preferences
  let login: LoginItem
  var body: some View {
    VStack(alignment: .leading, spacing: 20) {
      Text("Settings").font(.title2.bold())
      Form {
        Section("General") {
          Toggle(isOn: Binding(get: { login.enabled }, set: { login.setEnabled($0) })) {
            Label("Launch at Login", systemImage: "power")
          }
          if let message = login.message {
            Text(message).font(.caption).foregroundStyle(.secondary)
          }
          Toggle(isOn: $preferences.notchEnabled) { Label("Notch Monitor", systemImage: "macbook") }
          Toggle(isOn: $preferences.menuEnabled) {
            Label("Menu Bar Icon", systemImage: "menubar.rectangle")
          }
        }
        Section("Appearance") {
          Picker(selection: $preferences.appearance) {
            ForEach(AppAppearance.allCases) { Text($0.title).tag($0) }
          } label: {
            Label("Appearance", systemImage: "circle.lefthalf.filled")
          }
        }
      }.formStyle(.grouped).scrollContentBackground(.hidden)
      Text("AgentMeter remains available from the Dock when the menu-bar icon is hidden.").font(
        .caption
      ).foregroundStyle(.secondary)
      Spacer(minLength: 0)
    }.padding(24).onAppear { login.synchronize() }
  }
}
private struct AboutView: View {
  var body: some View {
    VStack(spacing: 16) {
      MeterMark().scaleEffect(2).frame(height: 50)
      Text("AgentMeter").font(.title.bold())
      Text(
        "Version \(Bundle.main.object(forInfoDictionaryKey:"AgentMeterReleaseVersion") as? String ?? "1.1.1")"
      ).font(.callout).foregroundStyle(.secondary)
      Text("AI coding allowance, quietly at a glance.").font(.callout).foregroundStyle(.secondary)
    }.frame(maxWidth: .infinity, maxHeight: .infinity)
  }
}
