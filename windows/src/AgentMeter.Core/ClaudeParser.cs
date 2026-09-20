using System.Text.Json;

namespace AgentMeter.Core;

public static class ClaudeParser
{
    public const string AmbiguousAccountMessage = "Claude desktop history contains multiple organizations. Current account cannot be verified safely; usage is unavailable.";
    private static readonly (string Key, string Name)[] KnownWindows =
    [
        ("fh", "5 hours"), ("sd", "7 days"), ("so", "7 days · Opus"),
        ("oa", "7 days · OAuth apps"), ("cw", "7 days · Cowork"),
        ("om", "7 days · Omelette"), ("op", "Omelette promotional"), ("sn", "7 days · Sonnet")
    ];

    // Validates the observed cache format only. The provider must establish account
    // identity before publishing any parsed snapshot; this parser cannot do that.
    public static ProviderResult Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (!UniqueObject(root) || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema))
                return ProviderResult.Fail(FailureKind.Malformed);
            if (schema != 2)
                return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop usage-history format is unsupported.");
            if (!root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                return ProviderResult.Fail(FailureKind.Malformed);
            if (samples.GetArrayLength() > 25000) return ProviderResult.Fail(FailureKind.Malformed);
            if (samples.GetArrayLength() == 0)
                return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop has no usage observations.");

            // Account IDs are compared only in memory and never leave this parser.
            var organizations = new HashSet<string>(StringComparer.Ordinal);
            JsonElement latest = default;
            DateTimeOffset? latestAt = null;
            var unidentifiedAccount = false;
            foreach (var sample in samples.EnumerateArray())
            {
                if (!UniqueObject(sample) || !sample.TryGetProperty("t", out var timestamp) ||
                    timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var milliseconds) ||
                    !sample.TryGetProperty("u", out var usage) || !UniqueObject(usage))
                    return ProviderResult.Fail(FailureKind.Malformed);
                DateTimeOffset observedAt;
                try { observedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
                catch (ArgumentOutOfRangeException) { return ProviderResult.Fail(FailureKind.Malformed); }
                if (observedAt > now.AddMinutes(1))
                    return ProviderResult.Fail(FailureKind.Malformed, "Claude desktop observation has a future timestamp.");
                if (!sample.TryGetProperty("org", out var organization) || organization.ValueKind == JsonValueKind.Null)
                    unidentifiedAccount = true;
                else if (organization.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(organization.GetString()))
                    return ProviderResult.Fail(FailureKind.Malformed);
                else organizations.Add(organization.GetString()!);
                if (latestAt is null || observedAt > latestAt)
                {
                    latest = usage;
                    latestAt = observedAt;
                }
                else if (observedAt == latestAt)
                {
                    // Same-time contradictory samples have no defensible ordering.
                    if (usage.GetRawText() != latest.GetRawText())
                        return ProviderResult.Fail(FailureKind.Malformed, "Claude desktop has conflicting usage observations.");
                }
            }
            if (organizations.Count > 1) return ProviderResult.Fail(FailureKind.Unsupported, AmbiguousAccountMessage);
            if (unidentifiedAccount || organizations.Count == 0)
                return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop history does not identify its account; usage is unavailable.");

            var windows = new List<UsageWindow>();
            var invalidFields = false;
            foreach (var (key, name) in KnownWindows)
            {
                if (!latest.TryGetProperty(key, out var field) || field.ValueKind == JsonValueKind.Null) continue;
                double? used = field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out var value)
                    ? UsageWindow.ValidPercent(value) : null;
                invalidFields |= used is null;
                windows.Add(new UsageWindow(key, name, used, null));
            }
            if (windows.Count == 0)
                return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop exposes no supported usage windows.");
            return new ProviderResult(new UsageSnapshot(windows, latestAt!.Value,
                "Claude desktop cache · account not verified", IsCached: true),
                Detail: "Cached observation only. Reset times and current sign-in are unavailable." +
                    (invalidFields ? " Invalid percentages are shown as unavailable." : ""));
        }
        catch (JsonException) { return ProviderResult.Fail(FailureKind.Malformed); }
    }

    private static bool UniqueObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property => names.Add(property.Name));
    }
}
