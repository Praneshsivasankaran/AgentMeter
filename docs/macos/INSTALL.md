# AgentMeter for macOS beta

[Download macOS beta 3](https://github.com/Praneshsivasankaran/AgentMeter/releases/tag/macos-0.1.0-beta.3). It remains unsigned and unnotarized. A signed and notarized 1.0.0 build is planned after independent validation; it is not available yet. There is no Mac App Store or production DMG release yet.

Download the ZIP from the release page, check it against the accompanying SHA-256 file, expand it, move `AgentMeter.app` to Applications, and open it. The current ad-hoc build may be rejected by Gatekeeper. Do not disable Gatekeeper or change global security settings. Only proceed with a build you trust; use Apple's per-app **Open Anyway** option in System Settings → Privacy & Security if macOS offers it. If it does not, wait for the signed build or choose the [source-build route](BUILD.md).

## Provider setup

AgentMeter has no sign-in or API-key field. Install and sign in to the supported tools using their own official setup:

- [Codex CLI](https://developers.openai.com/codex/quickstart/), signed in with ChatGPT.
- [Standalone Claude Code](https://code.claude.com/docs/en/setup), signed in with the relevant subscription account.

Desktop apps are additional activity triggers. Usage is read through the corresponding local CLI, so use the same subscription account across clients. Claude Desktop alone does not provide AgentMeter's verified allowance source.

## What to expect

Open AgentMeter once to see status and usage. Close the window to leave it running. The menu-bar item or Dock icon reopens it.

The notch appears while a supported desktop app is frontmost with a visible window, or while a recognized interactive CLI session is open. Hover for detail; click to open AgentMeter. Other displays use a top-edge fallback. Settings control the notch, menu bar, appearance, and launch at login.

The binary targets macOS 14+. Runtime testing is currently limited to an Apple Silicon M2 MacBook Air. Intel is included but not physically tested.

## If something isn't working

- **Not installed:** install the local tool in a standard supported location.
- **Not signed in:** sign in through that tool, then refresh AgentMeter.
- **Unavailable:** an upstream update or temporary service failure may have interrupted access. One provider failing does not stop the other.
- **No notch:** check Notch Monitor is enabled and the relevant desktop app is frontmost/visible or an interactive CLI is open. Unknown CLI modes deliberately do not trigger it.
- **Unexpected privacy prompt:** do not grant unrelated access. Report the version and action that triggered it.

[Report a bug](https://github.com/Praneshsivasankaran/AgentMeter/issues/new/choose). Include your Mac model, macOS version, display arrangement, and tool version. Inspect/sanitize anything shared from `~/Library/Logs/AgentMeter`. Never upload authentication files, tokens, private conversations, or raw terminal history.
