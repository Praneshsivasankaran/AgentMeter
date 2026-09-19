# macOS distribution

The current beta is unsigned and unnotarized. Apple Developer enrollment is pending. These scripts prepare a future Developer ID release; they do not configure an Apple account or credentials. Existing beta assets must never be silently replaced.

## Local unsigned rehearsal

From the repository root, with Xcode 27 selected:

```sh
scripts/macos/build-release.sh
scripts/macos/sign-app.sh dist/macos/AgentMeter.app
scripts/macos/create-dmg.sh dist/macos/AgentMeter.app --unsigned
scripts/macos/notarize.sh dist/macos/AgentMeter.app
scripts/macos/verify-release.sh dist/macos/AgentMeter.app --unsigned
```

The signing and notarization scripts default to **plan only**. The other commands actually build, inspect, and create an **unsigned** local DMG. Output goes into ignored `dist/macos/`; DerivedData stays under `~/Library/Developer/Xcode/DerivedData/AgentMeterDistribution`. Existing artifacts are not overwritten. Move an earlier app/DMG aside before another run.

The DMG contains `AgentMeter.app` and an `Applications` shortcut. Its name uses `AgentMeterReleaseVersion` from the built bundle, with an `-unsigned` suffix for rehearsals. A SHA-256 sidecar is generated. Mount the rehearsal read-only with `hdiutil attach -readonly -nobrowse`, inspect both entries, verify the mounted app, and detach it. This is **NOT SIGNED / NOT NOTARIZED**, not a public release candidate.

The validator checks identity, version/build, macOS 14 target, expected bundle structure, privacy checks, and universal arm64/x86_64 architecture. Building for Intel is not physical Intel testing.

## Hardened Runtime audit

Expected app entitlements: **none**. App Sandbox remains intentionally disabled.

- Provider queries execute external tools with `posix_spawn`, pipes, bounded lifetime/output, and an isolated working directory. They do not load provider code into AgentMeter. This does not justify disabling library validation or adding JIT/unsigned-memory exceptions.
- Native process metadata, exit sources, NSWorkspace observation, and CoreGraphics window metadata do not require debugger/task-port, Accessibility, or screen-content capture access.
- SwiftUI, AppKit panels, status items, and system frameworks require no runtime exception.
- Launch at Login uses `SMAppService.mainApp`; no embedded helper is currently shipped. Registration must be rechecked in the final signed installed app.
- The current bundle contains no nested executable/framework. The validator deliberately rejects newly introduced nested code. If a later release adds it, review and sign each nested item inside-out with its own minimal entitlements before signing the outer app. Never use `codesign --deep` as a substitute for a signing plan (`--deep` verification is fine).

The future signing command enables Hardened Runtime with `--options runtime` and a secure timestamp. Compatibility above is a source/bundle audit, **not a claim that a Developer ID-signed build has been tested**. Validate the final signed app with real Codex and Claude Code before release. Do not add exception entitlements to make a failed check disappear.

See Apple's [notarization overview](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution), [custom workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow), and [Hardened Runtime guidance](https://help.apple.com/xcode/mac/current/en.lproj/devf87a2ac8f.html).

## Future signed release — after enrollment activates

Choose a **new** release version/build first, run the complete tests and privacy/secret/artifact audits, then build a fresh app. Do not reuse the published beta tag for changed bytes.

Set `DEVELOPER_ID_APPLICATION` to the exact real **Developer ID Application** identity installed in your Keychain. No certificate hash, private key, password, or identity is stored in these scripts. Do not use Apple Development, ad-hoc, or self-signed identities as substitutes.

Set `NOTARY_PROFILE` to an existing `notarytool` Keychain profile. Create it separately after enrollment, using Apple's interactive `notarytool store-credentials` workflow. Keychain profiles can hold the supported Apple ID/app-specific-password credentials or an appropriate App Store Connect API key. Keep `.p8` files outside the repository, with restricted access; do not place passwords in shell history, logs, CI output, or command examples. No profile or credential is configured by the rehearsal.

The following is **future work**, not part of unsigned validation:

```sh
scripts/macos/sign-app.sh dist/macos/AgentMeter.app --execute
scripts/macos/notarize.sh dist/macos/AgentMeter.app --execute
scripts/macos/create-dmg.sh dist/macos/AgentMeter.app --signed
# Replace <version> with the new version from the built app.
scripts/macos/notarize.sh 'dist/macos/AgentMeter-<version>-macos.dmg' --execute
scripts/macos/verify-release.sh 'dist/macos/AgentMeter-<version>-macos.dmg' --notarized
```

The app is submitted in a temporary ZIP and stapled first, so the app copied out of the DMG carries its ticket. The signed DMG is submitted and stapled afterward. `notarytool` must return **Accepted**; inspect its local log for warnings. Local notary results/logs stay in ignored `dist/macos/notary.*` and must not be published. The scripts use no deprecated `altool`.

Verification checks the Developer ID authority, secure timestamp, Hardened Runtime, empty entitlements, strict signature validity, staple, Gatekeeper, and final DMG SHA-256. Checksums are regenerated **after stapling**, which changes bytes. Verification scripts do not publish releases.

## Final downloaded-artifact smoke checklist

1. Build, sign, notarize, and staple the final app and DMG. Review all validation and notary logs.
2. Upload the final DMG and SHA-256 to a **new** GitHub release. Keep previous release bytes unchanged.
3. Download that published artifact through a browser on a clean test Mac. Verify SHA-256 before mounting.
4. Mount read-only; validate the DMG signature/ticket and the contained app. Drag AgentMeter to Applications and eject the DMG.
5. Launch through Finder/LaunchServices with Gatekeeper enabled. Verify acceptance without a security bypass.
6. Verify real Codex and Claude Code usage; notch appearance, hover, click, hide; menu, Settings, restart, and Quit/child cleanup.
7. Verify zero unexpected Documents, Desktop, Downloads, Apple Music/Media Library, Accessibility, Screen Recording, Automation, or Full Disk Access prompts. Preserve isolated provider working directories, Git ceiling/worktree isolation, and all existing privacy guards. Stop if an unrelated prompt appears.
8. Check Launch at Login against actual registration state, then restore the tester's preference. Record architecture and hardware actually tested.

Do not describe a release as signed, notarized, or tested on Intel until the corresponding checks have really completed.
