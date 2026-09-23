import AppKit
import SwiftUI

struct SetupView: View {
  @Bindable var model: Presentation
  @Bindable var flow: SetupFlow
  let finished: () -> Void
  var body: some View {
    VStack(alignment: .leading, spacing: 22) {
      HStack(spacing: 10) {
        MeterMark()
        Text("Llumi").font(.headline)
        Spacer()
        Text("Setup").foregroundStyle(.secondary)
      }
      Divider()
      ScrollView {
        VStack(alignment: .leading, spacing: 20) { content }
          .frame(maxWidth: .infinity, alignment: .leading)
      }
      Divider()
      HStack {
        if flow.step != .welcome { Button("Back") { flow.back() } }
        Spacer()
        Button(nextTitle) {
          if flow.step == .done { flow.complete(); finished() }
          else {
            flow.next()
            if flow.step == .verify { model.refreshAction() }
          }
        }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
      }
    }.padding(28).frame(minWidth: 540, idealWidth: 580, minHeight: 510, idealHeight: 580)
      .background(Color(nsColor: .windowBackgroundColor))
  }
  private var nextTitle: String {
    if flow.step == .welcome { return "Set Up Llumi" }
    if flow.step == .done { return "Start Llumi" }
    if flow.step == .verify && !flow.selected.contains(where: { status($0) == .ready }) {
      return "Finish Anyway"
    }
    return "Continue"
  }
  private func status(_ provider: ProviderID) -> SetupStatus {
    SetupStatus(snapshot: model.usage[provider] ?? UsageSnapshot(provider: provider))
  }
  @ViewBuilder private var content: some View {
    switch flow.step {
    case .welcome:
      Text("Track your AI coding usage.").font(.largeTitle.bold())
      Text("Llumi monitors usage from your locally installed Codex and Claude Code tools.")
      Text("Use Codex, Claude Code, or both. You only need the provider you use.").foregroundStyle(.secondary)
      Label("Your sign-in stays with your provider.", systemImage: "lock")
    case .providers:
      heading("Choose your providers", "Select either or both. You can also set them up later.")
      ForEach(ProviderID.allCases, id: \.self) { provider in
        Toggle(isOn: Binding(get: { flow.selected.contains(provider) }, set: {
          if $0 { flow.selected.insert(provider) } else { flow.selected.remove(provider) }
        })) {
          HStack {
            ProviderMark(provider: provider)
            Text(provider == .claude ? "Claude Code" : "Codex").font(.headline)
            Spacer()
            Text(model.installations[provider] == nil ? "Not detected" : "Installed")
              .font(.callout).foregroundStyle(.secondary)
          }
        }.toggleStyle(.checkbox).padding(.vertical, 10)
      }
      checkAgain
    case .codex: providerInstructions(.codex)
    case .claude: providerInstructions(.claude)
    case .verify:
      heading("Check your setup", "One ready provider is enough. You can finish and return to setup anytime.")
      CheckSetupView(model: model)
    case .preferences:
      Text("Make it yours").font(.title.bold())
      Text("These are the same preferences you’ll find in Settings.").foregroundStyle(.secondary)
      SettingsView(preferences: model.preferences, login: model.loginItem)
        .frame(height: 365)
    case .done:
      heading("You’re all set", "Llumi will keep your allowance up to date. Setup is always available from the Help menu.")
      statuses
      Text("The notch appears when you use a supported coding-agent session. Close the main window to keep monitoring in the background.")
        .foregroundStyle(.secondary)
    }
  }
  private func heading(_ title: String, _ subtitle: String) -> some View {
    VStack(alignment: .leading, spacing: 8) {
      Text(title).font(.title.bold())
      Text(subtitle).foregroundStyle(.secondary)
    }
  }
  private var statuses: some View {
    VStack(spacing: 16) {
      ForEach(ProviderID.allCases, id: \.self) { provider in
        HStack {
          ProviderMark(provider: provider)
          Text(provider == .claude ? "Claude Code" : "Codex")
          Spacer()
          Label(status(provider).rawValue, systemImage: status(provider) == .ready ? "checkmark.circle" : "circle.dotted")
            .foregroundStyle(.secondary)
        }
      }
    }.padding(.vertical, 8)
  }
  private var checkAgain: some View {
    HStack {
      Button("Check Again") { model.refreshAction() }.disabled(model.manuallyRefreshing)
      if model.manuallyRefreshing { ProgressView().controlSize(.small) }
    }
  }
  private func providerInstructions(_ provider: ProviderID) -> some View {
    VStack(alignment: .leading, spacing: 16) {
      heading(provider == .codex ? "Set up Codex" : "Set up Claude Code",
        "Llumi uses the locally installed command-line tool and its existing authentication. Your credentials stay with the provider.")
      Text(status(provider).rawValue).font(.callout.weight(.medium))
      Text("Open Terminal from Applications → Utilities. Copy each command, paste it into Terminal, then press Return.")
        .font(.callout).foregroundStyle(.secondary)
      CommandBlock(title: "1. Install \(provider == .codex ? "Codex" : "Claude Code")",
        explanation: provider == .codex ? "Installs Codex using Homebrew. Skip this if Codex is already installed." : "Runs Anthropic’s official installer. Skip this if Claude Code is already installed.",
        command: ProviderSetup.install(provider))
      if provider == .codex {
        Text("Homebrew is required for this command. If you don’t have it, the official guide also offers a standalone installer.")
          .font(.caption).foregroundStyle(.secondary)
      }
      CommandBlock(title: "2. Sign in", explanation: provider == .codex
        ? "Opens Codex’s sign-in flow. Choose your ChatGPT subscription account."
        : "Opens Claude Code’s sign-in flow. Choose your Claude subscription account, not Console/API billing.",
        command: ProviderSetup.login(provider))
      HStack {
        checkAgain
        Spacer()
        Link("Official setup guide ↗", destination: ProviderSetup.documentation(provider))
      }
      Text("Llumi never runs these commands for you. After signing in, choose Check Again. No prompts or coding tasks are needed.")
        .font(.caption).foregroundStyle(.secondary)
    }
  }
}

private struct CommandBlock: View {
  let title: String
  let explanation: String
  let command: String
  @State private var copied = false
  var body: some View {
    VStack(alignment: .leading, spacing: 7) {
      Text(title).font(.headline)
      Text(explanation).font(.callout).foregroundStyle(.secondary)
      HStack(spacing: 12) {
        Text(command).font(.system(.callout, design: .monospaced)).textSelection(.enabled)
          .frame(maxWidth: .infinity, alignment: .leading)
        Button(copied ? "Copied" : "Copy") {
          NSPasteboard.general.clearContents()
          copied = NSPasteboard.general.setString(command, forType: .string)
        }.accessibilityLabel("Copy \(title) command")
      }.padding(12).background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 8))
    }.onChange(of: command) { _, _ in copied = false }
  }
}

struct CheckSetupView: View {
  @Bindable var model: Presentation
  @State private var copied = false
  var body: some View {
    VStack(alignment: .leading, spacing: 16) {
      Text("Check Setup").font(.title2.bold())
      Text("Checks use the same provider refresh as Usage. Unknown means this check could not verify that step.")
        .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
      ForEach(ProviderID.allCases, id: \.self) { provider in
        let snapshot = model.usage[provider] ?? UsageSnapshot(provider: provider)
        VStack(alignment: .leading, spacing: 6) {
          Text(provider == .claude ? "Claude Code" : "Codex").font(.headline)
          Text(SetupStatus(snapshot: snapshot).rawValue)
          Text(SetupDiagnostic(snapshot).summary).font(.caption).foregroundStyle(.secondary)
        }
      }
      HStack {
        Button("Check Again") { copied = false; model.refreshAction() }.disabled(model.manuallyRefreshing)
        if model.manuallyRefreshing { ProgressView().controlSize(.small) }
        Spacer()
        Button(copied ? "Copied" : "Copy Diagnostics") {
          let report = SetupDiagnostics.report(model.usage,
            version: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
            build: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String)
          NSPasteboard.general.clearContents()
          copied = NSPasteboard.general.setString(report, forType: .string)
        }
      }
      Text("Copies only app/OS versions, architecture and provider status categories. Nothing is uploaded.")
        .font(.caption).foregroundStyle(.secondary)
    }.padding(20)
  }
}
