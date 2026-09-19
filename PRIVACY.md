# Privacy

AgentMeter has no account system, telemetry, analytics, or backend. It reads allowance through the Codex and Claude Code tools you have already installed and authenticated. Those tools may contact their own services under their own settings and terms; AgentMeter does not send model prompts during usage checks.

AgentMeter does not copy or store provider credentials. Account continuity is checked in memory so a result cannot silently cross accounts. Usage snapshots stay in memory, and no usage history is saved.

On macOS, activity detection uses executable identity, process lifetime, a bounded invocation-mode check, and frontmost/on-screen window metadata. It does not read window titles, terminal contents, keystrokes, screen pixels, prompts, responses, or source repositories. Usage helpers run in isolated temporary working directories, outside user worktrees.

Preferences stay in local system preferences. Small rotating diagnostic logs contain health events, timings, and failure categories rather than raw provider responses or account identity. Mac logs are in `~/Library/Logs/AgentMeter`. Inspect and sanitize any diagnostic material before sharing it.

Normal Mac operation should not request Documents, Desktop, Downloads, Music/Media Library, Accessibility, Screen Recording, Automation, or Full Disk Access. An unexpected request is a bug: do not grant it just to make AgentMeter work; report the version and action that triggered it.


AgentMeter is independent of the supported providers. Their own applications and services retain their separate privacy policies.
