# Repository working agreement

- Preserve working provider authentication, subprocess isolation, activity detection and privacy behavior.
- Keep changes scoped and run relevant tests and the production privacy check.
- Use synthetic fixtures; never commit credentials, raw provider output, logs or personal development artifacts.
- Commit validated requested changes using the existing human Git identity. Push or publish only when the task authorizes it.
- Do not add AI attribution or co-author trailers.
- Start product behavior changes in docs/product-spec; consider both macOS and Windows and document intentional native differences.
- This public repository is the canonical product and source home. Preserve existing releases; source changes do not authorize publishing binaries.
- Windows direct distribution requires the gates in docs/DISTRIBUTION.md. Never publish a Store submission artifact as a GitHub download by default.
