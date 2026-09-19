# AgentMeter

AgentMeter is a lightweight native macOS utility for monitoring Codex and Claude Code usage.

It runs locally, detects supported sessions, and shows remaining allowance through a compact MacBook notch monitor, menu-bar utility, and full usage window. No AgentMeter account. No telemetry.

![AgentMeter's real contextual notch monitor appearing and expanding on hover](assets/demo/agentmeter-macos.gif)

## Download — macOS Beta

**[AgentMeter for macOS — Beta](https://github.com/Praneshsivasankaran/AgentMeter/releases/tag/macos-0.1.0-beta.3)** · 0.1.0 beta 3 · macOS 14 or later

The current beta is unsigned while Apple Developer enrollment is being processed. A signed and notarized build is planned next. Gatekeeper may block this build; read the [installation notes](docs/macos/INSTALL.md), or [build from source](docs/macos/BUILD.md). Do not disable Gatekeeper.

## What it does

- Shows verified Codex and Claude Code allowance, with explicit loading, stale, and unavailable states.
- Appears around the notch while a supported session is active; uses a top-edge monitor on other Macs.
- Expands on hover for the primary allowance and reset time; opens the full Usage window on click.
- Provides menu-bar controls, Launch at Login, and System, Light, and Dark appearance.

Desktop apps count while frontmost with a visible window. Recognized interactive CLI sessions count while open, including when idle. The monitor disappears when activity ends.

<img src="assets/screenshots/macos/usage.png" width="760" alt="AgentMeter Usage window showing Codex and Claude Code remaining allowance and reset times">

[Expanded notch](assets/screenshots/macos/notch-expanded.png) · [Settings](assets/screenshots/macos/settings.png)

## How it works

AgentMeter reads usage every 30 seconds through your existing locally installed, authenticated Codex CLI and standalone Claude Code. Desktop apps can trigger the monitor, but allowance comes from those CLI tools. Use the same subscription account across clients. AgentMeter never asks for API keys. Provider interfaces can change; unknown CLI modes deliberately do not trigger activity.

## Privacy

AgentMeter runs locally with no account, telemetry, analytics, or backend. It does not inspect prompts, responses, source code, or terminal contents, and does not store provider credentials. Authentication remains with the provider tools, which contact their own services. [Privacy details](PRIVACY.md).

## Build from source

[Build and test instructions](docs/macos/BUILD.md) · Swift, SwiftUI, and AppKit in `macos/`.

Release builds include Apple Silicon and Intel. Runtime testing is currently on an M2 MacBook Air; Intel and broader display coverage need beta testing.

## Testing the beta

[Try the short beta checklist](docs/macos/BETA-TESTING.md) and [report bugs](https://github.com/Praneshsivasankaran/AgentMeter/issues/new/choose). Please omit credentials and private content. [Contributing](CONTRIBUTING.md) · [Security reports](SECURITY.md).

## License

AgentMeter source is [MIT licensed](LICENSE). [Third-party components, logos, and trademarks retain their respective terms](THIRD-PARTY-NOTICES.md). AgentMeter is not affiliated with or endorsed by OpenAI or Anthropic.
