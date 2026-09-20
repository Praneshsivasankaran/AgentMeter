using System.Diagnostics;
using System.Text.Json;
using AgentMeter.Core;

// Opt-in live acceptance only. Never included in the application/package.
internal static class AcceptanceChecks
{
    internal static async Task Run(bool includeUsage = true)
    {
        var scanner = new WindowsActivitySource();
        var codex = CliLocator.FindCodex() ?? throw new Exception("Codex missing");
        var claude = ClaudeCliLocator.Find() ?? throw new Exception("Claude missing");
        var directory = Path.Combine(Path.GetTempPath(), "AgentMeter-LiveAcceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Emit(object data) => Console.WriteLine(JsonSerializer.Serialize(data));
        Process Start(string executable, params string[] arguments)
        {
            // A real Windows console session, hidden; no terminal output is captured/read.
            var info = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = directory, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            return Process.Start(info) ?? throw new Exception("CLI failed to start");
        }
        async Task<double> AwaitCli(bool cx, bool cl)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 8)
            {
                var state = scanner.Capture();
                if (state.Codex.Cli == cx && state.Claude.Cli == cl) return Math.Round(watch.Elapsed.TotalMilliseconds, 1);
                await Task.Delay(100);
            }
            throw new Exception($"CLI state not reached: Codex={cx}, Claude={cl}");
        }
        async Task Stop(Process p) { if (!p.HasExited) p.Kill(true); await p.WaitForExitAsync(); p.Dispose(); }
        var baseline = scanner.Capture();
        if (baseline.Codex.Cli || baseline.Claude.Cli) throw new Exception("Close existing interactive CLIs before isolated acceptance");
        foreach (var reverse in new[] { false, true })
        {
            Process? first = null, second = null;
            try
            {
                first = reverse ? Start(claude, "--safe-mode", "--effort", "high") : Start(codex);
                var start1 = await AwaitCli(!reverse, reverse);
                await Task.Delay(4000);
                if (first.HasExited) throw new Exception("Interactive CLI exited while idle");
                await AwaitCli(!reverse, reverse);
                second = reverse ? Start(codex, "--no-alt-screen") : Start(claude);
                var start2 = await AwaitCli(true, true);
                var both = scanner.Capture();
                if (both.Providers.Length != 2) throw new Exception("Provider deduplication failed");
                await Task.Delay(3000);
                if (first.HasExited || second.HasExited) throw new Exception("Combined CLI idle session exited");
                await Stop(first); first = null;
                var exit1 = await AwaitCli(reverse, !reverse);
                await Stop(second); second = null;
                var exit2 = await AwaitCli(false, false);
                Emit(new { check = "interactive-sequence", reverse, start1Ms = start1, start2Ms = start2, exit1Ms = exit1, exit2Ms = exit2, both, idleSeconds = 7, terminalRead = false, exitMethod = "owned process termination; normal user exit is separate" });
            }
            finally { if (first is not null) await Stop(first); if (second is not null) await Stop(second); }
        }
        foreach (var mode in new[] {
            ("Codex", codex, new[] {"--version"}), ("Codex", codex, new[] {"--help"}), ("Codex", codex, new[] {"login", "status"}),
            ("Codex", codex, new[] {"app-server", "--listen", "stdio://", "-c", "analytics.enabled=false"}),
            ("Claude Code", claude, new[] {"--version"}), ("Claude Code", claude, new[] {"--help"}), ("Claude Code", claude, new[] {"auth", "status"}),
            ("Claude Code", claude, new[] {"--print", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--safe-mode", "--setting-sources=", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}"}) })
        {
            var helper = Start(mode.Item2, mode.Item3); var samples = 0;
            try
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 2)
                {
                    var state = scanner.Capture(); samples++;
                    if (state.Codex.Cli || state.Claude.Cli) throw new Exception("Helper caused interactive activity");
                    if (helper.HasExited) break;
                    await Task.Delay(100);
                }
                Emit(new { check = "helper-excluded", provider = mode.Item1, mode = mode.Item3[0], samples });
            }
            finally { await Stop(helper); }
        }
        if (includeUsage) foreach (IUsageProvider provider in new IUsageProvider[] { new CodexProvider(), new ClaudeProvider() })
        {
            var watch = Stopwatch.StartNew(); var query = provider.QueryAsync(CancellationToken.None); var samples = 0;
            while (!query.IsCompleted)
            {
                var state = scanner.Capture(); samples++;
                if (state.Codex.Cli || state.Claude.Cli) throw new Exception("Owned query self-triggered activity");
                await Task.Delay(50);
            }
            var result = await query;
            Emit(new { check = "live-provider", provider = provider.Name, seconds = watch.Elapsed.TotalSeconds, samples, result.Failure, result.Snapshot, selfTriggered = false });
            if (result.Failure != FailureKind.None) throw new Exception("Live query failed");
        }
        try { Directory.Delete(directory); } catch (IOException) { }
    }
}
