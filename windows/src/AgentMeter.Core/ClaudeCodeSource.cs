using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentMeter.Core;

/// <summary>Detects standalone Claude Code independently; authentication alone does not verify usage.</summary>
public sealed class ClaudeCodeSource : IClaudeUsageSource
{
    private readonly Func<string?> _locate;
    private readonly Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>> _authQuery;
    private readonly Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>> _usageQuery;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeSpan _timeout;

    public ClaudeCodeSource(Func<string?>? locate = null,
        Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>>? authQuery = null,
        TimeSpan? timeout = null,
        Func<string, CancellationToken, Task<(JsonElement Output, int ExitCode)>>? usageQuery = null,
        Func<DateTimeOffset>? utcNow = null, string? bridgeDirectory = null)
    {
        _locate = locate ?? ClaudeCliLocator.Find;
        _authQuery = authQuery ?? ReadAuthAsync;
        _usageQuery = usageQuery ?? ((executable, token) => ReadUsageAsync(executable,
            bridgeDirectory ?? Path.Combine(AppContext.BaseDirectory, "ClaudeBridge"), token));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _timeout = timeout ?? TimeSpan.FromSeconds(12);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(12)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public ClaudeClient Client => ClaudeClient.Code;

    public async Task<ClaudeSourceResult> QueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var readingUsage = false;
        try
        {
            var executable = _locate();
            if (executable is null) return Fail(ClaudeAuthentication.Missing, FailureKind.NotInstalled, "Claude Code CLI was not found");
            var response = await _authQuery(executable, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var root = response.Output;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                return Malformed();
            var authProperties = root.EnumerateObject().Where(x => x.NameEquals("loggedIn")).ToArray();
            if (authProperties.Length != 1 || authProperties[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Malformed();
            var loggedIn = authProperties[0].Value.GetBoolean();
            // Observed official behavior: signed out exits 1; authenticated status exits 0.
            // Contradictory output must not promote an unknown client to authenticated.
            if (loggedIn && response.ExitCode != 0) return Malformed();
            if (!loggedIn && response.ExitCode is not (0 or 1))
                return Fail(ClaudeAuthentication.Unknown, FailureKind.ProcessExited, "Claude Code status command failed");
            if (!loggedIn) return Fail(ClaudeAuthentication.SignedOut, FailureKind.LoggedOut, "Claude Code is not authenticated");
            var binding = AuthBinding(root);
            if (binding is null)
                return Fail(ClaudeAuthentication.Authenticated, FailureKind.Unsupported, "Claude Code is authenticated; account metadata could not be verified");
            readingUsage = true;
            var usage = await _usageQuery(executable, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return ClaudeUsageParser.Parse(usage.Output, usage.ExitCode, _utcNow(), binding);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.Timeout, "Claude Code status query timed out"); }
        catch (JsonException) { return Malformed(); }
        catch (ProviderQueryException error)
        {
            if (readingUsage && error.Failure == FailureKind.Unsupported)
                return Fail(ClaudeAuthentication.Authenticated, FailureKind.Unsupported, "Claude Code usage helper is unavailable");
            return Fail(ClaudeAuthentication.Unknown, error.Failure, "Claude Code query failed");
        }
        catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3)
        {
            return readingUsage
                ? Fail(ClaudeAuthentication.Authenticated, FailureKind.Unsupported, "Claude Code usage helper is unavailable")
                : Fail(ClaudeAuthentication.Missing, FailureKind.NotInstalled, "Claude Code CLI was not found");
        }
        catch (UnauthorizedAccessException)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.AccessDenied, "Claude Code CLI could not be accessed"); }
        catch (Win32Exception error) when (error.NativeErrorCode == 5)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.AccessDenied, "Claude Code CLI could not be accessed"); }
        catch (Win32Exception)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.ProcessExited, "Claude Code CLI could not be started"); }
        catch (IOException)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.ProcessExited, "Claude Code status query failed"); }
        catch (Exception)
        { return Fail(ClaudeAuthentication.Unknown, FailureKind.Unexpected, "Claude Code status query failed"); }
    }

    private static async Task<(JsonElement Output, int ExitCode)> ReadAuthAsync(string executable, CancellationToken cancellationToken)
    {
        // Official opt-out applies only to this child; saved provider settings are untouched.
        // https://code.claude.com/docs/en/env-vars
        await using var process = ProviderProcess.Start(executable, ["auth", "status"], new Dictionary<string, string>
        {
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1"
        });
        return await process.ReadJsonOutputAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(JsonElement Output, int ExitCode)> ReadUsageAsync(string executable, string directory,
        CancellationToken cancellationToken)
    {
        var node = Path.Combine(directory, "node.exe");
        var script = Path.Combine(directory, "usage.mjs");
        if (!File.Exists(node) || !File.Exists(script) || !File.Exists(Path.Combine(directory, "sdk.mjs")))
            throw new ProviderQueryException(FailureKind.Unsupported);
        // The bridge must inherit the user's environment. The provider privacy flag
        // used by auth/status would disable subscription-usage retrieval here.
        await using var process = ProviderProcess.Start(node, [script, "--cli", executable]);
        return await process.ReadJsonOutputAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ClaudeAccountBinding? AuthBinding(JsonElement root)
    {
        static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out var property) &&
            property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        var email = Text(root, "email");
        var organization = Text(root, "orgId");
        if (Text(root, "authMethod") != "claude.ai" || Text(root, "apiProvider") != "firstParty" ||
            string.IsNullOrWhiteSpace(email) || email.Length > 320 || email.Any(char.IsControl) ||
            !Guid.TryParseExact(organization, "D", out var organizationId)) return null;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return new("claude-email-sha256:" + Convert.ToHexString(digest).ToLowerInvariant(), organizationId.ToString("D"));
    }

    private ClaudeSourceResult Malformed() => Fail(ClaudeAuthentication.Unknown, FailureKind.Malformed, "Claude Code returned an unrecognized status format");
    private ClaudeSourceResult Fail(ClaudeAuthentication authentication, FailureKind failure, string detail) =>
        new(Client, authentication, null, ProviderResult.Fail(failure, detail));
}
