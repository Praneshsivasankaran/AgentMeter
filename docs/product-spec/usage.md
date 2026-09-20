# Usage
AgentMeter is one product with native platform implementations. Its primary destinations are Usage and Settings. Provider names are Codex and Claude Code. The three ascending monochrome bars identify AgentMeter.

Usage presents each provider's status, primary remaining percentage, slim progress, meaningful windows, reset information and observation age. Remaining means 100 minus verified used percentage. Unknown is a dash, never zero or full. Codex primary is the longest reported main/core window; extra/Spark buckets never substitute. Claude primary is seven_day, falling back to five_hour. Provider-defined supplementary names retain their meaning; nimbus_quill is excluded from user-facing windows, matching the current Mac presentation.

Show Loading, Live, Stale, Not installed, Not signed in and Unavailable. A refresh never erases the last observation age or silently makes stale data live. Past resets await a new verified reading and never imply a refill. Full window detail belongs here, not in the compact monitor.

Refresh automatically every 30 seconds. Manual Refresh enters the same per-provider scheduler. One in-flight query per provider, coalesced repeated requests, bounded time/output, cancellation and complete owned-helper cleanup are required. A slow or failed provider cannot block the other.
