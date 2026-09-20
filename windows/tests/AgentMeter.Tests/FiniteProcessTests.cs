using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class FiniteProcessTests
{
    private static ProviderProcess Start(string script)
    {
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        return ProviderProcess.Start(shell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
    }

    [Fact]
    public async Task EnvironmentOverrideBelongsOnlyToChildProcess()
    {
        var variable = "AGENTMETER_TEST_" + Guid.NewGuid().ToString("N");
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnet = Path.Combine(runtime.Parent!.Parent!.Parent!.FullName, "dotnet.exe");
        await using var process = ProviderProcess.Start(dotnet,
            [Path.Combine(AppContext.BaseDirectory, "AgentMeter.ProcessHost.dll"), "--environment", variable],
            new Dictionary<string, string> { [variable] = "synthetic-child-value" });
        // This asserts child-only inheritance, not cold interpreter/module performance.
        // The fixture remains bounded; explicit cancellation tests below keep their short deadlines.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await process.ReadJsonOutputAsync(timeout.Token);
        Assert.Equal("synthetic-child-value", response.Output.GetProperty("value").GetString());
        Assert.Null(Environment.GetEnvironmentVariable(variable));
    }

    [Fact]
    public async Task PrettyJsonAndNonzeroExitArePreservedWhileStderrIsDiscarded()
    {
        await using var process = Start("[Console]::Error.Write(('x' * 100000)); [Console]::Out.WriteLine('{'); [Console]::Out.WriteLine('\"loggedIn\":false'); [Console]::Out.WriteLine('}'); exit 1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await process.ReadJsonOutputAsync(timeout.Token);
        Assert.Equal(1, response.ExitCode);
        Assert.False(response.Output.GetProperty("loggedIn").GetBoolean());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"loggedIn\":")]
    [InlineData("{}{}")]
    [InlineData("{} trailing-output")]
    public async Task InvalidOrConcatenatedOutputCannotBecomeAStatus(string output)
    {
        await using var process = Start($"[Console]::Out.Write('{output.Replace("'", "''")}')");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<JsonException>(() => process.ReadJsonOutputAsync(timeout.Token));
    }

    [Fact]
    public async Task EmptyOutputIsControlledFailure()
    {
        await using var process = Start("exit 0");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var failure = await Assert.ThrowsAsync<ProviderQueryException>(() => process.ReadJsonOutputAsync(timeout.Token));
        Assert.Equal(FailureKind.ProcessExited, failure.Failure);
    }

    [Fact]
    public async Task LongOutputIsBounded()
    {
        await using var process = Start("[Console]::Out.Write(('x' * 200000)); [Console]::Out.Flush(); [System.Threading.Tasks.Task]::Delay(60000).GetAwaiter().GetResult()");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var failure = await Assert.ThrowsAsync<ProviderQueryException>(() => process.ReadJsonOutputAsync(timeout.Token));
        Assert.Equal(FailureKind.Malformed, failure.Failure);
    }

    [Fact]
    public async Task ManyShortLinesCannotBypassTotalOutputLimit()
    {
        await using var process = Start("$line = 'x' * 1000; for ($i = 0; $i -lt 1100; $i++) { [Console]::Out.WriteLine($line) }");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var failure = await Assert.ThrowsAsync<ProviderQueryException>(() => process.ReadJsonOutputAsync(timeout.Token));
        Assert.Equal(FailureKind.Malformed, failure.Failure);
    }

    [Fact]
    public async Task ClosingStdoutDoesNotBypassProcessExitTimeout()
    {
        await using var process = Start("[Console]::Out.WriteLine('{}'); [Console]::Out.Close(); [System.Threading.Tasks.Task]::Delay(60000).GetAwaiter().GetResult()");
        using var owned = Process.GetProcessById(process.ProcessId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.ReadJsonOutputAsync(timeout.Token));
        await process.DisposeAsync();
        Assert.True(owned.HasExited);
    }

    [Fact]
    public async Task HangingFiniteCommandIsCanceledAndKilled()
    {
        await using var process = Start("[Console]::Out.WriteLine('{\"loggedIn\":true}'); [System.Threading.Tasks.Task]::Delay(60000).GetAwaiter().GetResult()");
        using var owned = Process.GetProcessById(process.ProcessId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.ReadJsonOutputAsync(timeout.Token));
        await process.DisposeAsync();
        Assert.True(owned.HasExited);
    }
}
