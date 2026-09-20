using System.Globalization;
using System.Text.Json;

namespace AgentMeter.Core;

public static class CodexParser
{
    public static ProviderResult Parse(JsonElement result, DateTimeOffset observedAt)
    {
        if (!UniqueObject(result)) return ProviderResult.Fail(FailureKind.Malformed);
        var windows = new List<UsageWindow>();
        var notices = new HashSet<string>();
        ReadHealth(result, notices);
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind != JsonValueKind.Null)
        {
            if (!UniqueObject(buckets)) return ProviderResult.Fail(FailureKind.Malformed);
            foreach (var bucket in buckets.EnumerateObject().OrderBy(b => b.Name == "codex" ? 0 : 1).ThenBy(b => b.Name, StringComparer.Ordinal))
                if (!ReadBucket(bucket.Value, SafeName(bucket.Name, "Codex"), windows, notices)) return ProviderResult.Fail(FailureKind.Malformed);
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind != JsonValueKind.Null)
        {
            if (!ReadBucket(legacy, "Codex", windows, notices)) return ProviderResult.Fail(FailureKind.Malformed);
        }
        else return ProviderResult.Fail(FailureKind.Unsupported);

        if (windows.Count == 0) return ProviderResult.Fail(FailureKind.Unsupported, "No usage windows are exposed for this account.");
        return new ProviderResult(new UsageSnapshot(windows, observedAt, "Codex app-server (live)"),
            Detail: notices.Count == 0 ? null : string.Join(" ", notices));
    }

    private static bool ReadBucket(JsonElement bucket, string id, List<UsageWindow> windows, HashSet<string> notices)
    {
        if (!UniqueObject(bucket)) return false;
        var name = bucket.TryGetProperty("limitName", out var label) && label.ValueKind == JsonValueKind.String
            ? SafeName(label.GetString(), id) : id;
        ReadHealth(bucket, notices);

        foreach (var slot in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(slot, out var window) || window.ValueKind == JsonValueKind.Null) continue;
            if (!UniqueObject(window)) return false;
            double? used = window.TryGetProperty("usedPercent", out var percent) && percent.ValueKind == JsonValueKind.Number && percent.TryGetDouble(out var value)
                ? UsageWindow.ValidPercent(value) : null;
            long? minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetInt64(out var mins) && mins > 0 ? mins : null;
            DateTimeOffset? reset = null;
            if (window.TryGetProperty("resetsAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out var seconds))
            {
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); }
                catch (ArgumentOutOfRangeException) { }
            }
            windows.Add(new UsageWindow($"{id}/{slot}", $"{name} · {DurationName(minutes)}", used, reset, minutes));
        }
        return true;
    }

    private static void ReadHealth(JsonElement bucket, HashSet<string> notices)
    {
        if (bucket.TryGetProperty("spendControlReached", out var spend) && spend.ValueKind == JsonValueKind.True)
            notices.Add("Provider reports a spend limit reached.");
        if (bucket.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind == JsonValueKind.False)
            notices.Add("Provider reports ordinary usage is unavailable.");
        if (bucket.TryGetProperty("rateLimitReachedType", out var reached) && reached.ValueKind == JsonValueKind.String &&
            reached.GetString() is { Length: > 0 } reachedType && !string.Equals(reachedType, "none", StringComparison.OrdinalIgnoreCase))
            notices.Add("Provider reports a reached limit.");

    }

    private static string DurationName(long? minutes) => minutes switch
    {
        null => "Window (duration unavailable)",
        1 => "1 minute",
        60 => "1 hour",
        1440 => "1 day",
        var value when value % 1440 == 0 => $"{(value / 1440).Value.ToString(CultureInfo.InvariantCulture)} days",
        var value when value % 60 == 0 => $"{(value / 60).Value.ToString(CultureInfo.InvariantCulture)} hours",
        var value => $"{value.Value.ToString(CultureInfo.InvariantCulture)} minutes"
    };

    private static string SafeName(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var safe = new string(text.Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
        return safe.Length == 0 ? fallback : safe;
    }

    private static bool UniqueObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(p => names.Add(p.Name));
    }
}
