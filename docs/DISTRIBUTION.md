# Official distribution

GitHub is the canonical home for Llumi's product documentation, issues, source and direct releases.

| Platform | Store channel | GitHub Releases |
| --- | --- | --- |
| macOS | Mac App Store planned; not published | Current unsigned public beta |
| Windows | Microsoft Store submission candidate; not published | Coming soon; no public binary |

The macOS beta remains a beta until Developer ID signing and notarization are complete. Apple signing/notarization and Mac App Store publication are distinct gates. See the [macOS distribution guide](macos/DISTRIBUTION.md).

## Windows direct-download requirements

A Microsoft Store submission MSIX is not automatically a direct-download artifact. Before publishing a Windows GitHub binary, review the exact proposed artifact and complete each gate:

| Gate | Required evidence |
| --- | --- |
| Final dependency and license inventory | Every shipped file, hash, version, origin and applicable license; exact runtime-pack notices if bundled |
| Redistribution rights | Verify terms for every included component, including runtime and Windows SDK projections; retain required notices |
| Provider payload exclusion | No Claude Agent SDK, `sdk.mjs`, bundled Claude/Codex executable or Node runtime; test the final payload |
| Signing and trust | Choose the direct artifact format and signing identity; verify signatures, timestamps, download provenance and Windows trust behavior |
| Clean installation | Test on a clean supported Windows system without development tools; verify launch, prerequisites, startup, uninstall and data handling |
| Updates | Define authenticated update delivery, version compatibility, data preservation, rollback and coexistence with the Store installation |
| Security and privacy | Audit the final binary and source, dependency advisories, provider isolation, logging, credential boundaries and clean-machine behavior |

These are release gates, not claims that a Windows direct release is approved. Build and CI success alone do not complete them. Keep internal audit receipts, raw diagnostics, signing credentials and Store submission material outside the public repository.

Update README download links only after a destination is publicly live. Until then, show **Coming soon**. Preserve existing releases and their status when adding a platform.
