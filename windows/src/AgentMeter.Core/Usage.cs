namespace AgentMeter.Core;

public enum ProviderStatus { Loading, Ready, Unavailable, Error }
public enum FailureKind { None, NotInstalled, LoggedOut, Unsupported, Timeout, Network, Malformed, ProcessExited, AccessDenied, Unexpected }

public sealed record UsageWindow
{
    public UsageWindow(string id, string name, double? usedPercent, DateTimeOffset? resetsAt, long? durationMinutes = null)
    { Id = id; Name = name; UsedPercent = ValidPercent(usedPercent); ResetsAt = resetsAt; DurationMinutes = durationMinutes; }
    public long? DurationMinutes { get; }
    public string Id { get; }
    public string Name { get; }
    public double? UsedPercent { get; }
    public DateTimeOffset? ResetsAt { get; }
    public double? RemainingPercent => UsedPercent is >= 0 and <= 100 && double.IsFinite(UsedPercent.Value) ? 100 - UsedPercent : null;
    public bool ResetPassed(DateTimeOffset now) => ResetsAt is { } reset && reset <= now;
    public static double? ValidPercent(double? value) => value is >= 0 and <= 100 && double.IsFinite(value.Value) ? value : null;
}

public sealed record UsageSnapshot(IReadOnlyList<UsageWindow> Windows, DateTimeOffset ObservedAt, string Source, bool IsCached = false);
public sealed record ProviderResult(UsageSnapshot? Snapshot, FailureKind Failure = FailureKind.None, string? Detail = null)
{
    public static ProviderResult Fail(FailureKind kind, string? safeDetail = null) => new(null, kind, safeDetail);
}

public interface IUsageProvider
{
    string Name { get; }
    Task<ProviderResult> QueryAsync(CancellationToken cancellationToken);
}

public sealed record ProviderState(string Name, ProviderStatus Status, UsageSnapshot? Snapshot = null,
    FailureKind Failure = FailureKind.None, string? Detail = null, DateTimeOffset? LastRefresh = null)
{
    public bool IsStale(DateTimeOffset now) => Snapshot is { } s &&
        (s.IsCached || Failure != FailureKind.None || now - s.ObservedAt > TimeSpan.FromMinutes(2) || s.ObservedAt > now.AddMinutes(1)
         || s.Windows.Any(w => w.ResetPassed(now)));
}

public static class FailureText
{
    public static string For(FailureKind failure) => failure switch
    {
        FailureKind.NotInstalled => "Not installed or executable not found",
        FailureKind.LoggedOut => "Signed out — sign in through the provider",
        FailureKind.Unsupported => "Usage is unavailable through this provider interface",
        FailureKind.Timeout => "Provider query timed out",
        FailureKind.Network => "Provider service or network is unavailable",
        FailureKind.Malformed => "Provider returned an unrecognized usage format",
        FailureKind.ProcessExited => "Provider exited before returning usage",
        FailureKind.AccessDenied => "Provider data could not be accessed",
        FailureKind.Unexpected => "Provider query failed",
        _ => ""
    };
}
