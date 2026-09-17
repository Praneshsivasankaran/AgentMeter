# Contributing

Fork the repository, create a branch, and keep the change focused. [Build and test the macOS application](docs/macos/BUILD.md) in `macos/`.



Tests should use synthetic data and must not require a real provider account. Keep provider authentication provider-owned. Preserve subprocess isolation, output limits, account checks, and explicit stale states.

Do not commit credentials, raw provider output, logs, personal screenshots, or build artifacts. Review screenshots for private content. Run the relevant tests and Mac privacy check before opening a pull request; explain the behavior changed and what you verified.
