using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class ActivityParityTests
{
    [Theory]
    [InlineData("Codex", "codex.exe", true)]
    [InlineData("Codex", "codex.exe --no-alt-screen --sandbox workspace-write", true)]
    [InlineData("Codex", "codex.exe --search --oss", true)]
    [InlineData("Codex", "codex.exe app-server", false)]
    [InlineData("Codex", "codex.exe --version", false)]
    [InlineData("Codex", "codex.exe exec", false)]
    [InlineData("Codex", "codex.exe --sandbox", false)]
    [InlineData("Codex", "codex.exe --sandbox unknown", false)]
    [InlineData("Claude Code", "claude.exe", true)]
    [InlineData("Claude Code", "claude.exe --safe-mode --effort high", true)]
    [InlineData("Claude Code", "claude.exe --verbose --no-chrome", true)]
    [InlineData("Claude Code", "claude.exe auth status", false)]
    [InlineData("Claude Code", "claude.exe --print", false)]
    [InlineData("Claude Code", "claude.exe --help", false)]
    [InlineData("Claude Code", "claude.exe private-prompt", false)]
    [InlineData("Claude Code", "claude.exe --unknown", false)]
    public void InteractiveGrammarFailsClosedForHelperAndUnknownModes(string provider, string invocation, bool expected) =>
        Assert.Equal(expected, ActivityPolicy.Interactive(provider, invocation, provider == "Codex" ? "codex.exe" : "claude.exe"));

    [Fact]
    public void StartupReadFailureMustNotHideSecondProviderForItsEntireLifetime()
    {
        var cache = new ActivityModeCache();
        var key = (42, 123L);
        Assert.False(cache.Read(key, () => false)); // process parameters not ready
        Assert.True(cache.Read(key, () => true)); // next ordinary sample
        Assert.True(cache.Read(key, () => throw new Exception("verified mode is cached")));
        cache.Retain([]);
        Assert.False(cache.Read(key, () => false));
        Assert.False(cache.Read((42, 124), () => false)); // PID reuse
        for (var i = 0; i < 3; i++) Assert.False(cache.Read((7, 1), () => false)); // helper never guessed active
    }

    [Fact]
    public void BoundedReaderStopsBeforeReadingPossiblePromptContents()
    {
        const string command = "codex.exe PRIVATE-PROMPT-MUST-NOT-BE-COLLECTED";
        var maxRead = 0;
        Assert.False(ActivityPolicy.Interactive("Codex", i => { maxRead = Math.Max(i, maxRead); return command[i]; }, "codex.exe", command.Length));
        Assert.Equal("codex.exe ".Length, maxRead);
        Assert.True(ActivityPolicy.Interactive("Codex", "\"C:\\Program Files\\Codex\\codex.exe\" --search", @"C:\Program Files\Codex\codex.exe"));
    }

    [Fact]
    public void DesktopSwitchMinimizeAndIndependentCliComposeWithoutDuplicates()
    {
        Assert.True(ActivityPolicy.DesktopActive(true, true, false));
        Assert.False(ActivityPolicy.DesktopActive(false, true, false));
        Assert.False(ActivityPolicy.DesktopActive(true, true, true));
        Assert.False(ActivityPolicy.DesktopActive(true, false, false));
        Assert.Equal(["Codex", "Claude Code"], new ActivitySnapshot(new(true, true), new(true, true)).Providers);
        Assert.Equal(["Codex"], new ActivitySnapshot(new(true, false), new(false, false)).Providers);
        Assert.Empty(ActivitySnapshot.Empty.Providers);
    }

    [Fact]
    public void DesktopIdentityDoesNotConfuseChatGptOrArbitraryExecutableWithCodex()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        Assert.Equal("Codex", WindowsActivitySource.DesktopProvider(Path.Combine(root, "OpenAI.Codex_1_x64__2p2nqsd0c76g0", "app", "ChatGPT.exe")));
        Assert.Null(WindowsActivitySource.DesktopProvider(Path.Combine(root, "OpenAI.ChatGPT-Desktop_1_x64__2p2nqsd0c76g0", "app", "ChatGPT.exe")));
        Assert.Null(WindowsActivitySource.DesktopProvider(@"C:\untrusted\Codex.exe"));
        Assert.Null(WindowsActivitySource.DesktopProvider(Path.Combine(root, "Claude_1_x64__wrong", "app", "Claude.exe")));
    }

    [Fact]
    public void PreferencesRoundTripAndMalformedDataFallsBackWithoutAccountState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentMeter.PreferenceTest." + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "preferences.json"); var store = new PreferenceStore(path);
        try
        {
            Assert.Equal(new Preferences(), store.Load());
            var desired = new Preferences(false, false, Appearance.Light);
            Assert.True(store.Save(desired)); Assert.Equal(desired, store.Load());
            Assert.DoesNotContain("account", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(path, "{malformed}"); Assert.Equal(new Preferences(), store.Load());
            File.WriteAllText(path, "{\"Appearance\":999}"); Assert.Equal(new Preferences(), store.Load());
        }
        finally { File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task OwnedUsageHelperNeverCountsAsInteractiveAndIsUnregisteredAfterCleanup()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "AgentMeter.ProcessHost.exe");
        var process = ProviderProcess.Start(host, []);
        var pid = process.ProcessId; Assert.True(ProviderProcess.IsOwned(pid));
        await process.DisposeAsync(); Assert.False(ProviderProcess.IsOwned(pid));
    }
}
