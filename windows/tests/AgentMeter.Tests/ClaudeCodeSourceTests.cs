using System.ComponentModel;
using System.Text.Json;
using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class ClaudeCodeSourceTests
{
    private static (JsonElement, int) Response(string json, int exitCode = 0)
    {
        using var document = JsonDocument.Parse(json);
        return (document.RootElement.Clone(), exitCode);
    }

    private static ClaudeCodeSource Source(string json, int exitCode = 0) =>
        new(() => "synthetic-cli.exe", (_, _) => Task.FromResult(Response(json, exitCode)));

    [Fact]
    public async Task MissingCliDoesNotAttemptAuthQuery()
    {
        var queried = false;
        var source = new ClaudeCodeSource(() => null, (_, _) => { queried = true; throw new InvalidOperationException(); });
        var result = await source.QueryAsync(CancellationToken.None);
        Assert.False(queried);
        Assert.Equal(ClaudeClient.Code, result.Client);
        Assert.Equal(ClaudeAuthentication.Missing, result.Authentication);
        Assert.Equal(FailureKind.NotInstalled, result.Usage.Failure);
    }

    [Fact]
    public async Task RealSourceLaunchesAuthStatusWithChildPrivacyFlag()
    {
        const string flag = "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC";
        var previous = Environment.GetEnvironmentVariable(flag);
        var source = new ClaudeCodeSource(() => Path.Combine(AppContext.BaseDirectory, "AgentMeter.ProcessHost.exe"));
        var result = await source.QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.SignedOut, result.Authentication);
        Assert.Equal(FailureKind.LoggedOut, result.Usage.Failure);
        Assert.Equal(previous, Environment.GetEnvironmentVariable(flag));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SignedOutCliIsIndependentOfDesktop(int exitCode)
    {
        var result = await Source("{\"loggedIn\":false,\"authMethod\":\"none\"}", exitCode).QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.SignedOut, result.Authentication);
        Assert.Equal(FailureKind.LoggedOut, result.Usage.Failure);
        Assert.Null(result.Binding);
        Assert.Null(result.Usage.Snapshot);
    }

    [Fact]
    public async Task AuthenticatedCliDoesNotClaimVerifiedQuotaOrRetainIdentifiers()
    {
        var result = await Source("{\"loggedIn\":true,\"email\":\"synthetic@example.invalid\",\"accountId\":\"synthetic-account\"}")
            .QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Authenticated, result.Authentication);
        Assert.Equal(FailureKind.Unsupported, result.Usage.Failure);
        Assert.Null(result.Binding);
        Assert.Null(result.Usage.Snapshot);
        Assert.DoesNotContain("synthetic", result.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"loggedIn\":\"true\"}")]
    [InlineData("{\"loggedIn\":1}")]
    [InlineData("{\"loggedIn\":null}")]
    [InlineData("{\"LoggedIn\":true}")]
    [InlineData("{\"loggedIn\":false,\"loggedIn\":true}")]
    [InlineData("{\"accountId\":\"synthetic-account\"}")]
    public async Task UnexpectedAuthSchemaCannotBecomeAuthenticated(string json)
    {
        var result = await Source(json).QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.Malformed, result.Usage.Failure);
        Assert.Null(result.Binding);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task FailedExitCannotReportSuccessfulAuthentication(int exitCode)
    {
        var result = await Source("{\"loggedIn\":true}", exitCode).QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.Malformed, result.Usage.Failure);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task UnexpectedExitCannotReliablyClaimSignedOut(int exitCode)
    {
        var result = await Source("{\"loggedIn\":false}", exitCode).QueryAsync(CancellationToken.None);
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.ProcessExited, result.Usage.Failure);
    }

    [Fact]
    public async Task TimeoutIsBoundedAndDoesNotLeakExceptionText()
    {
        var source = new ClaudeCodeSource(() => "synthetic-cli.exe", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response("{}");
        }, TimeSpan.FromMilliseconds(30));
        var result = await source.QueryAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ClaudeAuthentication.Unknown, result.Authentication);
        Assert.Equal(FailureKind.Timeout, result.Usage.Failure);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new ClaudeCodeSource(() => "synthetic-cli.exe", async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Response("{}");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.QueryAsync(cancellation.Token));
    }

    [Fact]
    public async Task AlreadyCanceledCallDoesNotInspectFilesOrStartProcess()
    {
        var source = new ClaudeCodeSource(() => throw new InvalidOperationException("must not run"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.QueryAsync(new CancellationToken(true)));
    }

    public static IEnumerable<object[]> QueryErrors()
    {
        yield return [new JsonException("synthetic-private-output"), FailureKind.Malformed, ClaudeAuthentication.Unknown];
        yield return [new ProviderQueryException(FailureKind.Malformed), FailureKind.Malformed, ClaudeAuthentication.Unknown];
        yield return [new ProviderQueryException(FailureKind.ProcessExited), FailureKind.ProcessExited, ClaudeAuthentication.Unknown];
        yield return [new Win32Exception(2, "synthetic-private-output"), FailureKind.NotInstalled, ClaudeAuthentication.Missing];
        yield return [new Win32Exception(5, "synthetic-private-output"), FailureKind.AccessDenied, ClaudeAuthentication.Unknown];
        yield return [new Win32Exception(193, "synthetic-private-output"), FailureKind.ProcessExited, ClaudeAuthentication.Unknown];
        yield return [new UnauthorizedAccessException("synthetic-private-output"), FailureKind.AccessDenied, ClaudeAuthentication.Unknown];
        yield return [new IOException("synthetic-private-output"), FailureKind.ProcessExited, ClaudeAuthentication.Unknown];
        yield return [new InvalidOperationException("synthetic-private-output"), FailureKind.Unexpected, ClaudeAuthentication.Unknown];
    }

    [Theory]
    [MemberData(nameof(QueryErrors))]
    public async Task ProcessFailuresAreSafe(Exception error, FailureKind failure, ClaudeAuthentication authentication)
    {
        var source = new ClaudeCodeSource(() => "synthetic-cli.exe", (_, _) => throw error);
        var result = await source.QueryAsync(CancellationToken.None);
        Assert.Equal(authentication, result.Authentication);
        Assert.Equal(failure, result.Usage.Failure);
        Assert.DoesNotContain("synthetic-private-output", result.ToString());
        Assert.Null(result.Binding);
        Assert.Null(result.Usage.Snapshot);
    }

    [Fact]
    public async Task RepeatedQueriesRecheckAuthentication()
    {
        var count = 0;
        var source = new ClaudeCodeSource(() => "synthetic-cli.exe", (_, _) =>
            Task.FromResult(++count == 1 ? Response("{\"loggedIn\":true}") : Response("{\"loggedIn\":false}", 1)));
        Assert.Equal(ClaudeAuthentication.Authenticated, (await source.QueryAsync(CancellationToken.None)).Authentication);
        Assert.Equal(ClaudeAuthentication.SignedOut, (await source.QueryAsync(CancellationToken.None)).Authentication);
    }
}
