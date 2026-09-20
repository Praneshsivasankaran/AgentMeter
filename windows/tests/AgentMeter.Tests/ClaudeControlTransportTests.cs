using System.Text.Json;
using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class ClaudeControlTransportTests
{
    private const string Auth = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","email":"fixture@example.invalid","orgId":"00000000-0000-0000-0000-000000000042","orgName":"Fixture","subscriptionType":"pro","analyticsDisabled":false}""";
    private const string Usage = """{"rate_limits_available":true,"behaviors":null,"subscription_type":"pro","session":{"total_cost_usd":0,"total_api_duration_ms":0,"model_usage":{}},"rate_limits":{"five_hour":{"utilization":12,"resets_at":"2030-01-01T12:30:00Z"},"seven_day":{"utilization":0,"resets_at":null}}}""";
    private static JsonElement J(string value) => JsonDocument.Parse(value).RootElement.Clone();

    [Fact]
    public async Task DirectQueryRevalidatesAccountAndNeedsNoBridge()
    {
        var authCalls = 0;
        var source = new ClaudeControlTransport(() => "fixture.exe", (_, _) => { authCalls++; return Task.FromResult((J(Auth), 0)); },
            (_, identity, _) => { Assert.Equal("pro", identity.Plan); return Task.FromResult(J(Usage)); });
        var result = await source.QueryAsync(default);
        Assert.Equal(2, authCalls);
        Assert.Equal([88d, 100d], result.Usage.Snapshot!.Windows.Select(w => w.RemainingPercent!.Value));
        Assert.DoesNotContain("fixture@example", result.ToString());
        Assert.DoesNotContain(ClaudeControlTransport.Arguments, a => a.Contains("sdk", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("fixture@example.invalid", "changed@example.invalid")]
    [InlineData("pro", "max")]
    [InlineData("Fixture", "Other organization")]
    public async Task ChangedAccountContextDiscardsUsage(string before, string after)
    {
        var calls = 0;
        var source = new ClaudeControlTransport(() => "fixture.exe", (_, _) => Task.FromResult((J(++calls == 1 ? Auth : Auth.Replace(before, after)), 0)),
            (_, _, _) => Task.FromResult(J(Usage)));
        var result = await source.QueryAsync(default);
        Assert.Null(result.Binding); Assert.Null(result.Usage.Snapshot); Assert.Equal(FailureKind.Unsupported, result.Usage.Failure);
    }

    [Theory]
    [InlineData("\"total_cost_usd\":0", "\"total_cost_usd\":1")]
    [InlineData("\"total_api_duration_ms\":0", "\"total_api_duration_ms\":1")]
    [InlineData("\"model_usage\":{}", "\"model_usage\":{\"model\":1}")]
    [InlineData("\"behaviors\":null", "\"behaviors\":{}")]
    [InlineData("\"utilization\":12", "\"utilization\":101")]
    [InlineData("\"utilization\":12", "\"utilization\":\"12\"")]
    [InlineData("2030-01-01T12:30:00Z", "2030-01-01T12:30:00")]
    [InlineData("\"utilization\":12", "\"utilization\":12,\"utilization\":12")]
    public void MalformedOrInferenceBearingResponsesFailClosed(string old, string replacement) =>
        Assert.Throws<ProviderQueryException>(() => ClaudeControlTransport.ParseUsage(J(Usage.Replace(old, replacement)), "pro"));

    [Fact]
    public void SessionMustMatchProviderOwnedAuthentication()
    {
        var identity = ClaudeControlTransport.ParseIdentity(J(Auth), 0);
        ClaudeControlTransport.VerifySession(J("""{"email":"fixture@example.invalid","organization":"Fixture","apiProvider":"firstParty","apiKeySource":"none","tokenSource":"oauth"}"""), identity);
        Assert.Throws<ProviderQueryException>(() => ClaudeControlTransport.VerifySession(J("""{"email":"other@example.invalid","organization":"Fixture","apiProvider":"firstParty"}"""), identity));
        Assert.NotEqual(identity.Binding, (identity with { Plan = "max" }).Binding);
    }

    [Fact]
    public async Task FailedUsageRetainsOnlyReverifiedBinding()
    {
        var source = new ClaudeControlTransport(() => "fixture.exe", (_, _) => Task.FromResult((J(Auth), 0)),
            (_, _, _) => throw new ProviderQueryException(FailureKind.Network));
        var result = await source.QueryAsync(default);
        Assert.NotNull(result.Binding); Assert.Null(result.Usage.Snapshot); Assert.Equal(FailureKind.Network, result.Usage.Failure);
    }

    [Fact]
    public async Task TimeoutAndCancellationDoNotRetainIdentity()
    {
        var source = new ClaudeControlTransport(() => "fixture.exe", (_, _) => Task.FromResult((J(Auth), 0)),
            async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return J(Usage); }, TimeSpan.FromMilliseconds(30));
        var result = await source.QueryAsync(default);
        Assert.Equal(FailureKind.Timeout, result.Usage.Failure); Assert.Null(result.Binding);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.QueryAsync(cancel.Token));
    }

    [Fact]
    public void IsolatedWorkspaceOverridesWorktreeEnvironmentAndRemovesEmptyDirectory()
    {
        var work = new ProviderWorkspace();
        var info = new System.Diagnostics.ProcessStartInfo();
        info.Environment["GIT_DIR"] = "private-repo"; info.Environment["GIT_WORK_TREE"] = "private-repo";
        work.Configure(info);
        Assert.False(info.Environment.ContainsKey("GIT_DIR")); Assert.False(info.Environment.ContainsKey("GIT_WORK_TREE"));
        Assert.Equal(work.DirectoryPath, info.WorkingDirectory);
        Assert.Equal(Path.GetDirectoryName(work.DirectoryPath), info.Environment["GIT_CEILING_DIRECTORIES"]);
        work.Dispose(); Assert.False(Directory.Exists(work.DirectoryPath));
    }
}
