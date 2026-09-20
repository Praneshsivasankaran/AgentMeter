using AgentMeter.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentMeter.Tests;

public sealed class ClaudeCliLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AgentMeter-claude-locator-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> junctions = [];

    private ClaudeCliSearchEnvironment EnvironmentFor(string? path = null, string? configured = null) =>
        new(path, Path.Combine(root, "profile"), Path.Combine(root, "local"), Path.Combine(root, "roaming"), configured);

    private string FileAt(params string[] parts)
    {
        var file = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "Fixture only; never executed.");
        return file;
    }

    [Fact]
    public void MissingInstallationsReturnNull() => Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));

    [Fact]
    public void ArbitraryQuotedPathIsPortableAndHasPriority()
    {
        var path = FileAt("custom tools", "claude.exe");
        FileAt("profile", ".local", "bin", "claude.exe");
        Assert.Equal(path, ClaudeCliLocator.Find(EnvironmentFor($".;relative; \"{Path.GetDirectoryName(path)}\" ;")));
    }

    [Fact]
    public void NativeInstallIsDetectedOutsidePath()
    {
        var native = FileAt("profile", ".local", "bin", "claude.exe");
        FileAt("roaming", "Claude", "claude-code", "2.1.270", "claude.exe");
        Assert.Equal(native, ClaudeCliLocator.Find(EnvironmentFor()));
    }

    [Fact]
    public void DesktopInstallIsDiagnosticOnlyWithHighestVersionFirstAcrossKnownLocations()
    {
        FileAt("roaming", "Claude", "claude-code", "2.1.99", "claude.exe");
        var highest = FileAt("local", "Packages", "Claude_synthetic", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.270", "claude.exe");
        FileAt("profile", ".vscode", "extensions", "anthropic.claude-code-9.0.0-win32-x64", "resources", "native-binary", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        var managed = ClaudeCliLocator.Discover(EnvironmentFor());
        Assert.Equal(highest, managed.First(candidate => candidate.Origin == ClaudeCliOrigin.DesktopManaged).Path);
        Assert.All(managed, candidate => Assert.NotEqual(ClaudeCliOrigin.Standalone, candidate.Origin));
    }

    [Fact]
    public void DesktopVersionWithoutExecutableIsSkipped()
    {
        FileAt("roaming", "Claude", "claude-code", "2.1.999", "nested", "claude.exe");
        var valid = FileAt("roaming", "Claude", "claude-code", "2.1.270", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        var managed = Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor()));
        Assert.Equal(valid, managed.Path);
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, managed.Origin);
    }

    [Fact]
    public void VsCodeNativeBinaryIsClassifiedButNeverUsedAsStandaloneFallback()
    {
        FileAt("profile", ".vscode", "extensions", "anthropic.claude-code-2.1.99-win32-x64", "resources", "native-binary", "claude.exe");
        var current = FileAt("profile", ".vscode", "extensions", "anthropic.claude-code-2.1.220-win32-x64", "resources", "native-binary", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        var managed = ClaudeCliLocator.Discover(EnvironmentFor());
        Assert.Equal(current, managed[0].Path);
        Assert.All(managed, candidate => Assert.Equal(ClaudeCliOrigin.VsCodeManaged, candidate.Origin));
    }

    [Fact]
    public void UnexpectedDesktopAndExtensionNamesAreNotSearched()
    {
        FileAt("roaming", "Claude", "claude-code", "unknown", "claude.exe");
        FileAt("local", "Packages", "unrelated", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.270", "claude.exe");
        FileAt("profile", ".vscode", "extensions", "unrelated-2.1.220", "resources", "native-binary", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        Assert.Empty(ClaudeCliLocator.Discover(EnvironmentFor()));
    }

    [Fact]
    public void ExplicitAbsoluteOverrideWins()
    {
        var configured = FileAt("configured", "claude.exe");
        var path = FileAt("path", "claude.exe");
        Assert.Equal(configured, ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(path), configured)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude.exe")]
    [InlineData("missing.exe")]
    public void InvalidOverrideIsAuthoritative(string configured)
    {
        var path = FileAt("path", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(path), configured)));
    }

    [Fact]
    public void ShellShimsCannotBeReturned()
    {
        var shim = FileAt("path", "claude.cmd");
        FileAt("path", "claude.ps1");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(shim))));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(configured: shim)));
    }

    [Fact]
    public void EmptyKnownFoldersCannotBecomeWorkingDirectorySearches() =>
        Assert.Null(ClaudeCliLocator.Find(new(".;relative", "", "", "")));

    [Fact]
    public void ExcessiveVersionDirectoriesFailClosed()
    {
        for (var i = 0; i < 129; i++) FileAt("roaming", "Claude", "claude-code", $"2.1.{i}", "claude.exe");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        Assert.Empty(ClaudeCliLocator.Discover(EnvironmentFor()));
    }

    [Theory]
    [InlineData("desktop", ClaudeCliOrigin.DesktopManaged)]
    [InlineData("msix", ClaudeCliOrigin.DesktopManaged)]
    [InlineData("vscode", ClaudeCliOrigin.VsCodeManaged)]
    [InlineData("insiders", ClaudeCliOrigin.VsCodeManaged)]
    [InlineData("custom-vscode", ClaudeCliOrigin.VsCodeManaged)]
    public void ManagedBinaryOnPathIsExcludedAndNativeInstallWins(string installation, ClaudeCliOrigin expectedOrigin)
    {
        var managed = ManagedFile(installation);
        var native = FileAt("profile", ".local", "bin", "claude.exe");
        var environment = EnvironmentFor(Path.GetDirectoryName(managed));
        Assert.Equal(native, ClaudeCliLocator.Find(environment));
        var evidence = ClaudeCliLocator.Discover(environment).Single(candidate => candidate.Path == managed);
        Assert.Equal(expectedOrigin, evidence.Origin);
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("msix")]
    [InlineData("vscode")]
    [InlineData("custom-vscode")]
    public void ManagedPathEntryDoesNotHideLaterGenuinePathCli(string installation)
    {
        var managed = ManagedFile(installation);
        var genuine = FileAt("independent tools", "claude.exe");
        FileAt("profile", ".local", "bin", "claude.exe");
        var environment = EnvironmentFor($"{Path.GetDirectoryName(managed)};\"{Path.GetDirectoryName(genuine)}\"");
        Assert.Equal(genuine, ClaudeCliLocator.Find(environment));
    }

    [Theory]
    [InlineData("desktop", ClaudeCliOrigin.DesktopManaged)]
    [InlineData("msix", ClaudeCliOrigin.DesktopManaged)]
    [InlineData("vscode", ClaudeCliOrigin.VsCodeManaged)]
    [InlineData("custom-vscode", ClaudeCliOrigin.VsCodeManaged)]
    public void ManagedOverrideIsAuthoritativeButCannotBecomeStandalone(string installation, ClaudeCliOrigin expectedOrigin)
    {
        var managed = ManagedFile(installation);
        var genuine = FileAt("independent", "claude.exe");
        var environment = EnvironmentFor(Path.GetDirectoryName(genuine), managed);
        Assert.Null(ClaudeCliLocator.Find(environment));
        var evidence = Assert.Single(ClaudeCliLocator.Discover(environment));
        Assert.Equal(managed, evidence.Path);
        Assert.Equal(expectedOrigin, evidence.Origin);
    }

    [Fact]
    public void ManagedPathWithoutStandaloneIsUnavailable()
    {
        var desktop = ManagedFile("desktop");
        var vscode = ManagedFile("vscode");
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor($"{Path.GetDirectoryName(desktop)};{Path.GetDirectoryName(vscode)}")));
    }

    [Fact]
    public void UnknownVersionInsideManagedCacheStillCannotBecomeStandaloneThroughPath()
    {
        var managed = FileAt("roaming", "Claude", "claude-code", "preview-current", "claude.exe");
        var environment = EnvironmentFor(Path.GetDirectoryName(managed));
        Assert.Null(ClaudeCliLocator.Find(environment));
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, Assert.Single(ClaudeCliLocator.Discover(environment)).Origin);
    }

    [Fact]
    public void SimilarDirectoryNamesOutsideManagedRootsRemainValidStandaloneCandidates()
    {
        var independent = FileAt("roaming", "Claude", "claude-code-tools", "claude.exe");
        Assert.Equal(independent, ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(independent))));
        Assert.Equal(ClaudeCliOrigin.Standalone, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: independent))).Origin);
    }

    [Fact]
    public void DiagnosticDiscoveryDoesNotDuplicatePathAndKnownCacheCandidate()
    {
        var managed = ManagedFile("desktop");
        var directory = Path.GetDirectoryName(managed);
        var evidence = ClaudeCliLocator.Discover(EnvironmentFor($"{directory};\"{directory}\""));
        Assert.Equal(managed, Assert.Single(evidence).Path);
    }

    [Fact]
    public void PathDirectoryJunctionCannotDisguiseDesktopManagedBinary()
    {
        var managed = ManagedFile("desktop");
        var alias = Junction("apparently-independent-tools", Path.GetDirectoryName(managed)!);
        Assert.True(File.GetAttributes(alias).HasFlag(FileAttributes.ReparsePoint));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(alias)));
        var evidence = Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(alias)));
        Assert.Equal(managed, evidence.Path);
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, evidence.Origin);
    }

    [Fact]
    public void RedirectedManagedCacheRootIsComparedByPhysicalTarget()
    {
        var actual = FileAt("relocated-cache", "2.1.270", "claude.exe");
        var managedRoot = Path.Combine(root, "roaming", "Claude", "claude-code");
        Junction(managedRoot, Path.Combine(root, "relocated-cache"));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(actual))));
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: actual))).Origin);
    }

    [Fact]
    public void IndependentPathThroughDirectoryJunctionRemainsUsable()
    {
        var independent = FileAt("actual-independent-tools", "claude.exe");
        var alias = Junction("tool-alias", Path.GetDirectoryName(independent)!);
        Assert.Equal(independent, ClaudeCliLocator.Find(EnvironmentFor(alias)));
    }

    [SymbolicLinksFact]
    public void NativeExecutableSymlinkToManagedBinaryIsRejected()
    {
        var managed = ManagedFile("desktop");
        var native = Path.Combine(root, "profile", ".local", "bin", "claude.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        File.CreateSymbolicLink(native, managed);
        Assert.True(File.GetAttributes(native).HasFlag(FileAttributes.ReparsePoint));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(configured: native)));
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: native))).Origin);
    }

    [SymbolicLinksFact]
    public void NativeExecutableSymlinkToIndependentVersionRemainsUsable()
    {
        var independent = FileAt("profile", ".local", "share", "claude", "versions", "2.1.272.exe");
        var native = Path.Combine(root, "profile", ".local", "bin", "claude.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        File.CreateSymbolicLink(native, independent);
        Assert.Equal(independent, ClaudeCliLocator.Find(EnvironmentFor()));
        Assert.Equal(ClaudeCliOrigin.Standalone, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor())).Origin);
    }

    [Fact]
    public void ExtendedLengthAliasCannotDisguiseManagedOverrideOrPath()
    {
        var managed = ManagedFile("desktop");
        var extended = @"\\?\" + managed;
        Assert.True(File.Exists(extended));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(configured: extended)));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(extended))));
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: extended))).Origin);
    }

    [ShortNamesFact]
    public void ActualShortPathAliasCannotDisguiseManagedOverride()
    {
        var managed = ManagedFile("desktop");
        var shortPath = ShortPath(managed);
        Assert.NotEqual(managed, shortPath);
        Assert.True(File.Exists(shortPath));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor(configured: shortPath)));
        Assert.Equal(ClaudeCliOrigin.DesktopManaged, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: shortPath))).Origin);
    }

    [Fact]
    public void MetadataOnlyDiscoveryDoesNotNeedToReadLockedExecutableContents()
    {
        var path = FileAt("independent", "claude.exe");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(path, ClaudeCliLocator.Find(EnvironmentFor(Path.GetDirectoryName(path))));
        Assert.Equal(ClaudeCliOrigin.Standalone, Assert.Single(ClaudeCliLocator.Discover(EnvironmentFor(configured: path))).Origin);
    }

    [SymbolicLinksFact]
    public void BrokenNativeExecutableLinkIsUnavailable()
    {
        var native = Path.Combine(root, "profile", ".local", "bin", "claude.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        File.CreateSymbolicLink(native, Path.Combine(root, "missing", "claude.exe"));
        Assert.Null(ClaudeCliLocator.Find(EnvironmentFor()));
        Assert.Empty(ClaudeCliLocator.Discover(EnvironmentFor(configured: native)));
    }

    private string Junction(string name, string target)
    {
        var path = Path.IsPathFullyQualified(name) ? name : Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path $env:AGENTMETER_TEST_LINK -Target $env:AGENTMETER_TEST_TARGET -ErrorAction Stop | Out-Null");
        start.Environment["AGENTMETER_TEST_LINK"] = path;
        start.Environment["AGENTMETER_TEST_TARGET"] = target;
        using var process = Process.Start(start)!;
        // Junction creation is setup, not a provider latency assertion. A fresh
        // Windows runner may need to cold-load the PowerShell management module.
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Fixture junction creation timed out.");
        }
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        junctions.Add(path);
        return path;
    }

    private static string ShortPath(string path)
    {
        var buffer = new StringBuilder(32768);
        var length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    private sealed class SymbolicLinksFactAttribute : FactAttribute
    {
        public SymbolicLinksFactAttribute()
        {
            var directory = Directory.CreateTempSubdirectory("AgentMeter-symlink-capability-").FullName;
            var target = Path.Combine(directory, "target");
            var link = Path.Combine(directory, "link");
            try
            {
                File.WriteAllText(target, "Fixture");
                File.CreateSymbolicLink(link, target);
            }
            catch (UnauthorizedAccessException) { Skip = "Windows does not permit symbolic-link creation for this account."; }
            catch (IOException error) when ((error.HResult & 0xffff) == 1314)
            { Skip = "Windows symbolic-link privilege is unavailable."; }
            finally { File.Delete(link); File.Delete(target); Directory.Delete(directory); }
        }
    }

    private sealed class ShortNamesFactAttribute : FactAttribute
    {
        public ShortNamesFactAttribute()
        {
            var directory = Directory.CreateTempSubdirectory("AgentMeter-short-name-capability-").FullName;
            try
            {
                if (ShortPath(directory) == directory) Skip = "This filesystem does not provide 8.3 aliases.";
            }
            finally { Directory.Delete(directory); }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint capacity);

    private string ManagedFile(string installation) => installation switch
    {
        "desktop" => FileAt("roaming", "Claude", "claude-code", "2.1.270", "claude.exe"),
        "msix" => FileAt("local", "Packages", "Claude_synthetic", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.270", "claude.exe"),
        "vscode" => FileAt("profile", ".vscode", "extensions", "anthropic.claude-code-2.1.220-win32-x64", "resources", "native-binary", "claude.exe"),
        "insiders" => FileAt("profile", ".vscode-insiders", "extensions", "anthropic.claude-code-2.1.220-win32-x64", "resources", "native-binary", "claude.exe"),
        "custom-vscode" => FileAt("custom editor extensions", "anthropic.claude-code-2.1.220-win32-x64", "resources", "native-binary", "claude.exe"),
        _ => throw new ArgumentOutOfRangeException(nameof(installation))
    };

    public void Dispose()
    {
        foreach (var junction in junctions) Directory.Delete(junction);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
