using System.Text;
using System.Text.Json;
using AgentMeter.Core;

if (args.Length == 1 && args[0] == "--acceptance-activity") { await AcceptanceChecks.Run(includeUsage: false); return; }

if (args.Length == 1 && args[0] == "--acceptance") { await AcceptanceChecks.Run(); return; }

// Private validation harness; never packaged with the application.
if (args.Length == 2 && args[0] == "--activity-observe" && int.TryParse(args[1], out var seconds))
{
    var scanner = new WindowsActivitySource();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    using var self = System.Diagnostics.Process.GetCurrentProcess();
    var cpuBefore = self.TotalProcessorTime.TotalSeconds;
    ActivitySnapshot? previous = null;
    while (timer.Elapsed.TotalSeconds < Math.Clamp(seconds, 1, 180))
    {
        var current = scanner.Capture();
        if (current != previous) Console.WriteLine(JsonSerializer.Serialize(new { seconds = Math.Round(timer.Elapsed.TotalSeconds, 2), activity = current }));
        previous = current;
        await Task.Delay(500);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { elapsedSeconds = timer.Elapsed.TotalSeconds, cpuSeconds = self.TotalProcessorTime.TotalSeconds - cpuBefore }));
    return;
}

// A finite native fixture for environment inheritance. Keep this independent of
// PowerShell startup and ConvertTo-Json module autoload on a fresh Windows runner.
if (args.Length == 2 && args[0] == "--environment")
{
    Console.WriteLine(JsonSerializer.Serialize(new { value = Environment.GetEnvironmentVariable(args[1]) }));
    return;
}

// Test-only auth-status fixture verifies that the real source passes its child privacy flag.
if (args.SequenceEqual(new[] { "auth", "status" }))
{
    Console.WriteLine(JsonSerializer.Serialize(new { loggedIn = false }));
    Environment.ExitCode = Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC") == "1" ? 1 : 2;
    return;
}

// Test-only sacrificial parent: killing this host must close its Windows job.
var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
var grandchildScript = Convert.ToBase64String(Encoding.Unicode.GetBytes("[System.Threading.Tasks.Task]::Delay(60000).GetAwaiter().GetResult()"));
var script = $"$child = Start-Process -FilePath '{shell.Replace("'", "''")}' -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','{grandchildScript}' -PassThru -WindowStyle Hidden; [Console]::Out.WriteLine(('{{\"id\":1,\"result\":{{\"child\":' + $child.Id + '}}}}')); [System.Threading.Tasks.Task]::Delay(60000).GetAwaiter().GetResult()";
await using var query = ProviderProcess.Start(shell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var ready = await query.ReadRpcResultAsync(1, startup.Token);
Console.WriteLine(JsonSerializer.Serialize(new { child = query.ProcessId, grandchild = ready.GetProperty("child").GetInt32() }));
await Console.Out.FlushAsync(startup.Token);
await Task.Delay(Timeout.Infinite, startup.Token);
