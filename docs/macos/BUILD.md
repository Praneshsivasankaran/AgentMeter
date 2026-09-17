# Build AgentMeter for macOS

Use Xcode 27 with Swift 6.4 (the currently verified toolchain). The deployment target is macOS 14. Release builds contain arm64 and x86_64; Intel runtime behavior has not been physically tested. Earlier Xcode versions are not yet verified.

```sh
git clone https://github.com/Praneshsivasankaran/AgentMeter.git
cd AgentMeter
xcodebuild -project macos/AgentMeter.xcodeproj -scheme AgentMeter -configuration Release -derivedDataPath "$HOME/Library/Developer/Xcode/DerivedData/AgentMeter" -destination 'platform=macOS' clean build
xcodebuild -project macos/AgentMeter.xcodeproj -scheme AgentMeter -configuration Debug -derivedDataPath "$HOME/Library/Developer/Xcode/DerivedData/AgentMeter" -destination 'platform=macOS' test
python3 macos/Scripts/privacy-check.py "$HOME/Library/Developer/Xcode/DerivedData/AgentMeter/Build/Products/Release/AgentMeter.app"
open "$HOME/Library/Developer/Xcode/DerivedData/AgentMeter/Build/Products/Release/AgentMeter.app"
```

Build output belongs under Library, outside protected document folders. No provider login, provider installation, signing certificate, or third-party package installation is needed to compile or run synthetic tests. Real usage appears only when supported tools are installed and signed in.

To make a technical beta ZIP after validation:

```sh
mkdir -p "$HOME/Library/Caches/AgentMeterBeta"
ditto -c -k --sequesterRsrc --keepParent "$HOME/Library/Developer/Xcode/DerivedData/AgentMeter/Build/Products/Release/AgentMeter.app" "$HOME/Library/Caches/AgentMeterBeta/AgentMeter-0.1.0-beta.2-macos.zip"
shasum -a 256 "$HOME/Library/Caches/AgentMeterBeta/AgentMeter-0.1.0-beta.2-macos.zip"
```

This creates a local ad-hoc build, not Developer ID signing or notarization. Builds are source-reproducible; byte-identical archives are not promised. The macOS workflow uses GitHub's `xcode-27` preview runner with synthetic tests and no provider authentication or signing secrets. Preview runner availability may vary; no passing badge is advertised until a real run completes.
