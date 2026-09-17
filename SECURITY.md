# Security

AgentMeter is beta software. Provider authentication stays with the provider's tools; AgentMeter does not offer its own login or collect credentials.

Report security problems through [GitHub private vulnerability reporting](https://github.com/Praneshsivasankaran/AgentMeter/security/advisories/new). Do not post exploit details in public issues.

Never attach tokens, cookies, authentication files, raw provider responses, private conversations, terminal history, or source repositories. Start with the affected version, a failure category, and reproduction steps using synthetic data.

Locally installed provider executables are trusted programs. AgentMeter bounds their usage queries and cleans up owned children, but cannot protect against an attacker who can replace the user's executables or control the user's environment.

The Mac beta is not Developer ID signed or notarized.
