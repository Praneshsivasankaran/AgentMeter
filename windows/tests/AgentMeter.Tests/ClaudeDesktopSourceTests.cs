using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class ClaudeDesktopSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1789478000000);
    private const string History = """{"version":2,"samples":[{"t":1789477765995,"org":"fixture-org","u":{"fh":0,"sd":8}}]}""";

    [Fact]
    public async Task MissingDesktopMetadataIsDistinctFromSignedOut()
    {
        var source = new ClaudeDesktopSource(() => throw new InvalidOperationException("Must not read cache"), isDetected: () => false);
        var result = await source.QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeClient.Desktop, result.Client);
        Assert.Equal(ClaudeAuthentication.Missing, result.Authentication);
        Assert.Equal(FailureKind.NotInstalled, result.Usage.Failure);
        Assert.Null(result.Binding);
        Assert.Null(result.Usage.Snapshot);
    }

    [Fact]
    public async Task PresentDesktopWithMissingUsageDoesNotClaimItIsSignedOut()
    {
        var result = await new ClaudeDesktopSource(() => [], isDetected: () => true).QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.Unsupported, result.Usage.Failure);
        Assert.Null(result.Usage.Snapshot);
    }

    [Theory]
    [InlineData("fixture-org", true)]
    [InlineData("unrelated-account", true)]
    [InlineData("fixture-org", false)]
    [InlineData("", false)]
    public async Task LastKnownAccountAndSignedInLayoutFlagNeverBecomeAnAuthenticatedBinding(string account, bool layoutWasSignedIn)
    {
        var directory = TemporaryDirectory();
        try
        {
            var cache = Path.Combine(directory, "plan-usage-history.json");
            await File.WriteAllTextAsync(cache, History);
            // The same UUID text in different namespaces is not account-to-org proof.
            // Production deliberately never reads this secret-bearing settings file.
            await File.WriteAllTextAsync(Path.Combine(directory, "config.json"), System.Text.Json.JsonSerializer.Serialize(new
            { lastKnownAccountUuid = account, windowSizeWasSignedIn = layoutWasSignedIn }));
            var source = new ClaudeDesktopSource(() => [cache], () => Now, () => true);
            var result = await source.QueryAsync(CancellationToken.None);
            Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
            Assert.Null(result.Binding);
            Assert.Null(result.Usage.Snapshot);
            Assert.Equal(FailureKind.Unsupported, result.Usage.Failure);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MultipleHistoricalOrganizationsRemainUnverifiedEvenWhenOneIsNewest()
    {
        var directory = TemporaryDirectory();
        try
        {
            var cache = Path.Combine(directory, "plan-usage-history.json");
            await File.WriteAllTextAsync(cache, History.Replace("}]}", "},{\"t\":1789477965995,\"org\":\"another-org\",\"u\":{\"sd\":97}}]}"));
            var result = await new ClaudeDesktopSource(() => [cache], () => Now).QueryAsync(CancellationToken.None);
            Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
            Assert.Equal(ClaudeParser.AmbiguousAccountMessage, result.Usage.Detail);
            Assert.Null(result.Binding);
            Assert.Null(result.Usage.Snapshot);
            Assert.DoesNotContain("another-org", result.Usage.Detail!);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MalformedCacheIsAnErrorAndNotEvidenceOfLogout()
    {
        var directory = TemporaryDirectory();
        try
        {
            var cache = Path.Combine(directory, "plan-usage-history.json");
            await File.WriteAllTextAsync(cache, "{malformed");
            var result = await new ClaudeDesktopSource(() => [cache]).QueryAsync(CancellationToken.None);
            Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
            Assert.Equal(FailureKind.Malformed, result.Usage.Failure);
            Assert.Null(result.Usage.Snapshot);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DetectionAccessFailureNeverMasqueradesAsMissingClient()
    {
        var result = await new ClaudeDesktopSource(() => [], isDetected: () => throw new UnauthorizedAccessException())
            .QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.AccessDenied, result.Usage.Failure);
    }

    [Fact]
    public async Task CancelledDesktopQueryDoesNotReadMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new ClaudeDesktopSource(() => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.QueryAsync(cancellation.Token));
    }

    [Fact]
    public void OrdinaryAndPackagedMetadataAreDiscoveredWithoutRecursiveScanning()
    {
        var directory = TemporaryDirectory();
        try
        {
            var roaming = Path.Combine(directory, "roaming");
            var local = Path.Combine(directory, "local");
            var ordinary = Directory.CreateDirectory(Path.Combine(roaming, "Claude")).FullName;
            var packaged = Directory.CreateDirectory(Path.Combine(local, "Packages", "Claude_fixture", "LocalCache", "Roaming", "Claude")).FullName;
            Directory.CreateDirectory(Path.Combine(local, "Packages", "AnotherApp", "LocalCache", "Roaming", "Claude"));
            Assert.Equal(new[] { ordinary, packaged }, ClaudeDesktopSource.FindDataRoots(roaming, local));
            Assert.Empty(ClaudeDesktopSource.FindDataRoots("relative", "relative"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string TemporaryDirectory()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "AgentMeter.DesktopSourceTests." + Guid.NewGuid().ToString("N"))).FullName;
}
