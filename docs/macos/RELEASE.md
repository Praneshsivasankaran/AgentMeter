# macOS 0.1.0-beta.3

Public technical beta for monitoring Codex and Claude Code usage. Native usage window, menu-bar controls, and a contextual notch/top-edge monitor. Hover expands the primary allowance; clicking opens the application.

- macOS 14 deployment target; universal arm64/x86_64 executable.
- Physically tested on an Apple Silicon M2 MacBook Air. Intel and broader multi-display coverage remain unverified.
- Provider interfaces can change; unknown CLI invocation modes fail closed.
- Local ad-hoc build only. Developer ID signed: **No**. Notarized: **No**.
- Gatekeeper assessment: **rejected — no usable signature**. The archive is for users comfortable evaluating an unsigned technical beta; Apple Developer enrollment is being processed, and a signed and notarized build is planned next.

Release packaging strips debug-symbol paths from the distributed executable; symbols remain outside the application bundle. The privacy audit checks all binary sections.

The native bundle version is 0.1.0 (build 2), with release version 0.1.0-beta.3 shown in About.

Found something broken? [Open an issue](https://github.com/Praneshsivasankaran/AgentMeter/issues/new/choose) without credentials or private content.
