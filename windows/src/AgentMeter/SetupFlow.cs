using System.Text.Json;
using AgentMeter.Core;

namespace AgentMeter;

internal enum SetupStep { Welcome, Providers, Codex, Claude, Verify, Preferences, Done }
internal sealed class SetupFlow(SetupCompletionStore store)
{
    internal bool Codex { get; set; } = true;
    internal bool Claude { get; set; } = true;
    internal SetupStep Step { get; private set; } = SetupStep.Welcome;
    internal SetupStep[] Steps => new[] { SetupStep.Welcome, SetupStep.Providers }
        .Concat(Codex ? new[] { SetupStep.Codex } : []).Concat(Claude ? new[] { SetupStep.Claude } : [])
        .Concat([SetupStep.Verify, SetupStep.Preferences, SetupStep.Done]).ToArray();
    internal void Next() { var i = Array.IndexOf(Steps, Step); if (i >= 0 && i + 1 < Steps.Length) Step = Steps[i + 1]; }
    internal void Back() { var i = Array.IndexOf(Steps, Step); if (i > 0) Step = Steps[i - 1]; }
    internal void Reopen() => Step = SetupStep.Welcome;
    internal bool Complete() => store.Save(true);
}
internal sealed class SetupCompletionStore(string path)
{
    internal static SetupCompletionStore Default() => new(Path.Combine(PackagedEnvironment.DataDirectory, "setup-completed.json"));
    internal bool RecognizeExisting(bool existingPreferences)
    {
        if (File.Exists(path)) return IsComplete();
        Save(existingPreferences); // Persist fresh false before the user changes preferences.
        return existingPreferences;
    }
    internal bool IsComplete()
    {
        try
        {
            using var input = File.OpenRead(path);
            if (input.Length > 32) return false;
            using var value = JsonDocument.Parse(input);
            return value.RootElement.ValueKind == JsonValueKind.True;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
    internal bool Save(bool completed)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, completed ? "true" : "false");
            File.Move(temporary, path, true); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

internal sealed record SetupDiagnostic(string Detected, string Authentication, string Usage, string Failure, string Result)
{
    internal static SetupDiagnostic From(ProviderState state)
    {
        var failure = state.Failure switch {
            FailureKind.None => "none", FailureKind.NotInstalled => "notInstalled", FailureKind.LoggedOut => "signedOut",
            FailureKind.Unsupported => "incompatible", FailureKind.Timeout => "timeout", FailureKind.Malformed => "malformed",
            FailureKind.ProcessExited => "processExited", _ => "unavailable" };
        if (state.Status == ProviderStatus.Ready && state.Snapshot is not null && !state.IsStale(DateTimeOffset.UtcNow))
            return new("yes", "verified", "available", failure, "success");
        if (state.Failure == FailureKind.NotInstalled) return new("no", "unknown", "unavailable", failure, "failure");
        if (state.Failure == FailureKind.LoggedOut) return new("yes", "signed-out", "unavailable", failure, "failure");
        if (state.Status == ProviderStatus.Loading) return new("unknown", "unknown", "checking", failure, "pending");
        return new("unknown", "unknown", state.Snapshot is null ? "unavailable" : "stale", failure, "failure");
    }
    internal string Status => Usage == "available" ? "Ready" : Detected == "no" ? "Not installed" :
        Authentication == "signed-out" ? "Installed — sign in required" : Usage == "checking" ? "Checking…" : "Unavailable";
    internal string Summary => $"{Status}\nDetected: {Detected} · Authentication: {Authentication} · Usage: {Usage}";
}
internal static class SetupDiagnostics
{
    internal static string Numeric(string? value) => value is { Length: > 0 and <= 32 } &&
        value.Split('.').All(p => p.Length > 0 && p.All(c => c is >= '0' and <= '9')) ? value : "unknown";
    internal static string Report(IReadOnlyList<ProviderState> states, string? version, string? build)
    {
        var lines = new List<string> { "Llumi diagnostics schema: 1", $"App version: {Numeric(version)}",
            $"App build: {Numeric(build)}", $"OS: Windows {Environment.OSVersion.Version}",
            $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" };
        foreach (var provider in new[] { "Codex", "Claude" })
        {
            var state = states.FirstOrDefault(s => s.Name == provider || (provider == "Claude" && s.Name == "Claude Code"))
                ?? new ProviderState(provider, ProviderStatus.Loading);
            var value = SetupDiagnostic.From(state);
            lines.AddRange([$"[{provider.ToLowerInvariant()}]", $"Detected: {value.Detected}",
                $"Authentication: {value.Authentication}", $"Usage: {value.Usage}",
                $"Failure: {value.Failure}", $"Last result: {value.Result}"]);
        }
        return string.Join("\n", lines);
    }
}
