using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentMeter.Core;

/// <summary>Usage-only Claude Code control protocol. No SDK, prompt, or model request.</summary>
public sealed class ClaudeControlTransport : IClaudeUsageSource
{
    public ClaudeClient Client => ClaudeClient.Code;
    public static readonly string[] Arguments = ["--print", "--input-format", "stream-json", "--output-format", "stream-json",
        "--verbose", "--no-session-persistence", "--safe-mode", "--setting-sources=", "--strict-mcp-config",
        "--mcp-config", "{\"mcpServers\":{}}"];
    private readonly Func<string?> locate;
    private readonly Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>> auth;
    private readonly Func<string, Identity, CancellationToken, Task<JsonElement>> usage;
    private readonly TimeSpan timeout;

    public ClaudeControlTransport(Func<string?>? locate = null,
        Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>>? auth = null,
        Func<string, Identity, CancellationToken, Task<JsonElement>>? usage = null, TimeSpan? timeout = null)
    {
        this.locate = locate ?? ClaudeCliLocator.Find;
        this.auth = auth ?? ReadAuth;
        this.usage = usage ?? ReadUsage;
        this.timeout = timeout ?? TimeSpan.FromSeconds(12);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(12)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<ClaudeSourceResult> QueryAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            var executable = locate();
            if (executable is null) return Fail(FailureKind.NotInstalled);
            if (PrivacyOptOut()) return Fail(FailureKind.Unsupported);
            var beforeResult = await auth(executable, token).ConfigureAwait(false);
            var before = ParseIdentity(beforeResult.Output, beforeResult.ExitCode);
            JsonElement response = default;
            FailureKind failure = FailureKind.None;
            try { response = await usage(executable, before, token).ConfigureAwait(false); }
            catch (ProviderQueryException e) { failure = e.Failure; }
            catch (JsonException) { failure = FailureKind.Malformed; }
            var afterResult = await auth(executable, token).ConfigureAwait(false);
            var after = ParseIdentity(afterResult.Output, afterResult.ExitCode);
            token.ThrowIfCancellationRequested();
            if (before != after) return Fail(FailureKind.Unsupported);
            var binding = after.Binding;
            if (failure != FailureKind.None) return Fail(failure, binding);
            try
            {
                var windows = ParseUsage(response, after.Plan);
                return new(Client, ClaudeAuthentication.Authenticated, binding,
                    new(new UsageSnapshot(windows, DateTimeOffset.UtcNow, "Claude Code control protocol (live)")));
            }
            catch (ProviderQueryException e) { return Fail(e.Failure, e.Failure == FailureKind.Unsupported ? null : binding); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(FailureKind.Timeout); }
        catch (ProviderQueryException e) { return Fail(e.Failure); }
        catch (JsonException) { return Fail(FailureKind.Malformed); }
        catch (InvalidOperationException) { return Fail(FailureKind.Malformed); }
        catch (KeyNotFoundException) { return Fail(FailureKind.Malformed); }
        catch (UnauthorizedAccessException) { return Fail(FailureKind.AccessDenied); }
        catch (System.ComponentModel.Win32Exception e) { return Fail(e.NativeErrorCode is 2 or 3 ? FailureKind.NotInstalled : FailureKind.AccessDenied); }
        catch (IOException) { return Fail(FailureKind.ProcessExited); }
    }

    public sealed record Identity(string Email, string Organization, string OrganizationName, string Plan)
    {
        public ClaudeAccountBinding Binding => new("claude-context-sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Email + "\n" + Organization + "\n" + Plan))).ToLowerInvariant(), Organization);
        public override string ToString() => "Claude identity (omitted)";
    }

    public static Identity ParseIdentity(JsonElement value, int exitCode)
    {
        RequireUnique(value);
        var logged = Get(value, "loggedIn");
        if (logged.ValueKind == JsonValueKind.False && exitCode is 0 or 1) throw new ProviderQueryException(FailureKind.LoggedOut);
        if (logged.ValueKind != JsonValueKind.True || exitCode != 0) throw new ProviderQueryException(FailureKind.Malformed);
        var email = Text(value, "email");
        var organization = Text(value, "orgId");
        var name = Text(value, "orgName");
        var plan = Text(value, "subscriptionType");
        if (Text(value, "authMethod") != "claude.ai" || Text(value, "apiProvider") != "firstParty" ||
            plan is not ("pro" or "max" or "team" or "enterprise") || Get(value, "analyticsDisabled").ValueKind != JsonValueKind.False ||
            email is null || !email.Contains('@') || email.Any(char.IsWhiteSpace) || !Guid.TryParseExact(organization, "D", out var id) || name is null)
            throw new ProviderQueryException(FailureKind.Unsupported);
        return new(email.ToLowerInvariant(), id.ToString("D"), name, plan);
    }

    public static void VerifySession(JsonElement account, Identity identity)
    {
        RequireUnique(account);
        var key = Text(account, "apiKeySource");
        var token = Text(account, "tokenSource");
        foreach (var name in new[] { "apiKeySource", "tokenSource" })
            if (Get(account, name).ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String))
                throw new ProviderQueryException(FailureKind.Malformed);
        if (!string.Equals(Text(account, "email"), identity.Email, StringComparison.OrdinalIgnoreCase) ||
            Text(account, "apiProvider") != "firstParty" ||
            Text(account, "organization") is not { } org || (org != identity.Organization && org != identity.OrganizationName) ||
            key is not (null or "none") || token is not (null or "claude.ai" or "oauth" or "claudeAiOauth" or "CLAUDE_CODE_OAUTH_TOKEN"))
            throw new ProviderQueryException(FailureKind.Unsupported);
    }

    public static IReadOnlyList<UsageWindow> ParseUsage(JsonElement value, string plan)
    {
        RequireUnique(value);
        if (Get(value, "rate_limits_available").ValueKind != JsonValueKind.True ||
            !value.TryGetProperty("behaviors", out var behaviors) || behaviors.ValueKind != JsonValueKind.Null || Text(value, "subscription_type") != plan)
            throw new ProviderQueryException(FailureKind.Unsupported);
        var session = Get(value, "session");
        if (!Zero(Get(session, "total_cost_usd")) || !Zero(Get(session, "total_api_duration_ms")) ||
            Get(session, "model_usage") is not { ValueKind: JsonValueKind.Object } models || models.EnumerateObject().Any())
            throw new ProviderQueryException(FailureKind.Unsupported);
        var limits = Get(value, "rate_limits");
        if (limits.ValueKind != JsonValueKind.Object || limits.EnumerateObject().Count() > 64) throw new ProviderQueryException(FailureKind.Malformed);
        var windows = new List<UsageWindow>();
        foreach (var property in limits.EnumerateObject())
        {
            if (property.Name is "model_scoped" or "extra_usage" || property.Value.ValueKind == JsonValueKind.Null) continue;
            if (property.Name.Length > 100 || property.Name.Any(char.IsControl)) throw new ProviderQueryException(FailureKind.Malformed);
            if (property.Value.ValueKind != JsonValueKind.Object ||
                (!property.Value.TryGetProperty("utilization", out _) && !property.Value.TryGetProperty("resets_at", out _))) continue;
            var label = property.Name switch { "five_hour" => "5 hours", "seven_day" => "7 days", _ => property.Name };
            windows.Add(ReadWindow(property.Value, property.Name, label));
        }
        var scoped = Get(limits, "model_scoped");
        if (scoped.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (scoped.ValueKind != JsonValueKind.Array || scoped.GetArrayLength() > 32) throw new ProviderQueryException(FailureKind.Malformed);
            var seen = new HashSet<string>();
            foreach (var model in scoped.EnumerateArray())
            {
                var label = Text(model, "display_name");
                if (label is null || label.Length > 100 || !seen.Add(label)) throw new ProviderQueryException(FailureKind.Malformed);
                windows.Add(ReadWindow(model, "model:" + label, label));
            }
        }
        if (!windows.Any(w => w.UsedPercent is not null || w.ResetsAt is not null)) throw new ProviderQueryException(FailureKind.Unsupported);
        return windows;
    }

    private static UsageWindow ReadWindow(JsonElement value, string id, string name)
    {
        double? used = null;
        var percentage = Get(value, "utilization");
        if (percentage.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (percentage.ValueKind != JsonValueKind.Number || !percentage.TryGetDouble(out var n) || UsageWindow.ValidPercent(n) is null)
                throw new ProviderQueryException(FailureKind.Malformed);
            used = n;
        }
        DateTimeOffset? reset = null;
        var stamp = Get(value, "resets_at");
        if (stamp.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            var text = Text(value, "resets_at");
            if (text is null || !Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ProviderQueryException(FailureKind.Malformed);
            reset = parsed;
        }
        return new(id, name, used, reset);
    }

    private static async Task<(JsonElement, int)> ReadAuth(string executable, CancellationToken token)
    {
        await using var process = ProviderProcess.Start(executable, ["auth", "status"]);
        return await process.ReadJsonOutputAsync(token).ConfigureAwait(false);
    }
    private static async Task<JsonElement> ReadUsage(string executable, Identity identity, CancellationToken token)
    {
        await using var process = ProviderProcess.Start(executable, Arguments);
        await process.SendAsync(new { type = "control_request", request_id = "init", request = new { subtype = "initialize", hooks = new { } } }, token).ConfigureAwait(false);
        var initialized = await process.ReadControlResultAsync("init", token).ConfigureAwait(false);
        VerifySession(Get(initialized, "account"), identity);
        await process.SendAsync(new { type = "control_request", request_id = "usage", request = new { subtype = "get_usage", skip_behaviors = true } }, token).ConfigureAwait(false);
        return await process.ReadControlResultAsync("usage", token).ConfigureAwait(false);
    }
    public static void RequireUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!seen.Add(property.Name)) throw new ProviderQueryException(FailureKind.Malformed); RequireUnique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RequireUnique(item);
    }
    public static JsonElement Get(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    public static string? Text(JsonElement value, string key)
    {
        var element = Get(value, key);
        if (element.ValueKind != JsonValueKind.String) return null;
        var text = element.GetString();
        return !string.IsNullOrWhiteSpace(text) && text == text.Trim() && text.Length <= 320 && !text.Any(char.IsControl) ? text : null;
    }
    private static bool Zero(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && n == 0;
    private static bool PrivacyOptOut() => new[] { "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC", "DISABLE_TELEMETRY" }
        .Any(key => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) ||
        Environment.GetEnvironmentVariable("DO_NOT_TRACK") is "1" or "true";
    private static ClaudeSourceResult Fail(FailureKind kind, ClaudeAccountBinding? binding = null) => new(ClaudeClient.Code,
        kind == FailureKind.LoggedOut ? ClaudeAuthentication.SignedOut : kind == FailureKind.NotInstalled ? ClaudeAuthentication.Missing :
        binding is null ? ClaudeAuthentication.Unknown : ClaudeAuthentication.Authenticated, binding, ProviderResult.Fail(kind));
}
