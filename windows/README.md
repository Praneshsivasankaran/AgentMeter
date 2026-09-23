# Llumi for Windows

Native Windows Forms implementation of Llumi, using .NET 10.

**Status:** Microsoft Store submission candidate; Store publication pending. A GitHub direct download is coming soon and requires its own distribution review.

- [Build, test and run](../docs/windows/BUILD.md)
- [Shared product specification](../docs/product-spec/README.md)
- [Privacy](../PRIVACY.md)
- [Distribution requirements](../docs/DISTRIBUTION.md)
- [Third-party notices](../THIRD-PARTY-NOTICES.md)

`src/AgentMeter` contains the native UI and lifecycle integration. `src/AgentMeter.Core` contains provider retrieval, parsing, refresh scheduling and activity detection. `tests/` uses synthetic fixtures. The current application invokes separately installed provider tools and contains no Claude Agent SDK or Node bridge.

The source version retains the candidate's engineering identifier; it does not identify a public Windows binary release.
