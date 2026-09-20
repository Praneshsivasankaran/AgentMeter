using System.Runtime.InteropServices;
using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class CliLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AgentMeter-locator-tests", Guid.NewGuid().ToString("N"));

    private CliSearchEnvironment EnvironmentFor(string? path = null, Architecture architecture = Architecture.X64,
        string? configured = null) => new(path, Path.Combine(root, "local"), Path.Combine(root, "roaming"), architecture, configured);

    private string FileAt(params string[] parts)
    {
        var file = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "Fixture only; never executed.");
        return file;
    }

    [Fact]
    public void MissingProvidersReturnNull() => Assert.Null(CliLocator.FindCodex(EnvironmentFor()));

    [Fact]
    public void NativePathFindsExecutableUnderArbitraryUserDirectory()
    {
        var executable = FileAt("custom tools", "codex.exe");
        Assert.Equal(executable, CliLocator.FindCodex(EnvironmentFor(Path.GetDirectoryName(executable))));
    }

    [Fact]
    public void QuotedPathWhitespaceAndInvalidEntriesAreHandled()
    {
        var executable = FileAt("custom tools", "codex.exe");
        var path = $";.;relative;\0;  \"{Path.GetDirectoryName(executable)}\" ;";
        Assert.Equal(executable, CliLocator.FindOnPath("codex.exe", path));
    }

    [Fact]
    public void PathOrderIsPreserved()
    {
        var first = FileAt("first", "codex.exe");
        var second = FileAt("second", "codex.exe");
        Assert.Equal(first, CliLocator.FindCodex(EnvironmentFor($"{Path.GetDirectoryName(first)};{Path.GetDirectoryName(second)}")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("codex.exe")]
    [InlineData("missing.exe")]
    public void InvalidExplicitOverrideDoesNotFallBack(string configured)
    {
        var executable = FileAt("path", "codex.exe");
        Assert.Null(CliLocator.FindCodex(EnvironmentFor(Path.GetDirectoryName(executable), configured: configured)));
    }

    [Fact]
    public void AbsoluteOverrideWinsOverPath()
    {
        var preferred = FileAt("preferred", "codex.exe");
        var onPath = FileAt("path", "codex.exe");
        Assert.Equal(preferred, CliLocator.FindCodex(EnvironmentFor(Path.GetDirectoryName(onPath), configured: $"\"{preferred}\"")));
    }

    [Fact]
    public void ShellShimsAreNeverReturnedOrExecuted()
    {
        var shim = FileAt("path", "codex.cmd");
        FileAt("path", "codex.ps1");
        Assert.Null(CliLocator.FindCodex(EnvironmentFor(Path.GetDirectoryName(shim))));
        Assert.Null(CliLocator.FindOnPath("codex.cmd", Path.GetDirectoryName(shim)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("codex-win32-x64")]
    public void AlternateNpmPrefixOnPathResolvesNativeBinary(string package)
    {
        var executable = FileAt("alternate npm", "node_modules", "@openai", package,
            "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
        Assert.Equal(executable, CliLocator.FindCodex(EnvironmentFor(Path.Combine(root, "alternate npm"))));
    }

    [Fact]
    public void NestedNpmOptionalPackageIsDiscovered()
    {
        var executable = FileAt("alternate npm", "node_modules", "@openai", "codex", "node_modules", "@openai",
            "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
        Assert.Equal(executable, CliLocator.FindCodex(EnvironmentFor(Path.Combine(root, "alternate npm"))));
    }

    [Fact]
    public void DefaultRoamingNpmPrefixIsDiscoveredWhenNotOnPath()
    {
        var executable = FileAt("roaming", "npm", "node_modules", "@openai", "codex-win32-x64",
            "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
        Assert.Equal(executable, CliLocator.FindCodex(EnvironmentFor()));
    }

    [Fact]
    public void ArchitectureChoosesMatchingNpmBinary()
    {
        FileAt("roaming", "npm", "node_modules", "@openai", "codex-win32-x64",
            "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
        var arm = FileAt("roaming", "npm", "node_modules", "@openai", "codex-win32-arm64",
            "vendor", "aarch64-pc-windows-msvc", "codex", "codex.exe");
        Assert.Equal(arm, CliLocator.FindCodex(EnvironmentFor(architecture: Architecture.Arm64)));
        Assert.Null(CliLocator.FindCodex(EnvironmentFor(architecture: Architecture.X86)));
    }

    [Fact]
    public void DesktopFallbackUsesNewestExistingVersionWithoutDeepScanning()
    {
        var older = FileAt("local", "OpenAI", "Codex", "bin", "older", "codex.exe");
        var newer = FileAt("local", "OpenAI", "Codex", "bin", "newer", "codex.exe");
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(older)!, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(newer)!, DateTime.UtcNow.AddDays(-1));
        FileAt("local", "OpenAI", "Codex", "bin", "newest-but-nested", "other", "codex.exe");
        Assert.Equal(newer, CliLocator.FindCodex(EnvironmentFor()));
    }

    [Fact]
    public void PathNativeExecutableTakesPrecedenceOverDesktopFallback()
    {
        var executable = FileAt("path", "codex.exe");
        FileAt("local", "OpenAI", "Codex", "bin", "current", "codex.exe");
        Assert.Equal(executable, CliLocator.FindCodex(EnvironmentFor(Path.GetDirectoryName(executable))));
    }

    [Theory]
    [InlineData("../codex.exe")]
    [InlineData("sub\\codex.exe")]
    [InlineData("C:\\codex.exe")]
    public void PathLookupRejectsExecutableNamesContainingDirectories(string name) =>
        Assert.Null(CliLocator.FindOnPath(name, root));

    [Fact]
    public void EmptyKnownFoldersCannotSearchRelativeToWorkingDirectory() =>
        Assert.Null(CliLocator.FindCodex(new CliSearchEnvironment(".;relative", "", "", Architecture.X64)));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
