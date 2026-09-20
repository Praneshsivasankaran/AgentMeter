using System.Text;

namespace AgentMeter.Core;

public sealed class ClaudeDesktopSource : IClaudeUsageSource
{
    public const string UnverifiedAccountMessage = "Account could not be verified. Claude desktop history cannot establish the currently authenticated Claude account.";
    private const int MaximumBytes = 4 * 1024 * 1024;
    private readonly Func<IReadOnlyList<string>> findCaches;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<bool> isDetected;
    public ClaudeClient Client => ClaudeClient.Desktop;

    public ClaudeDesktopSource(Func<IReadOnlyList<string>>? findCaches = null, Func<DateTimeOffset>? utcNow = null, Func<bool>? isDetected = null)
    {
        this.findCaches = findCaches ?? FindUsageCaches;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.isDetected = isDetected ?? (() => findCaches is not null || FindDataRoots().Count > 0);
    }

    public async Task<ClaudeSourceResult> QueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!isDetected())
                return new(Client, ClaudeAuthentication.Missing, null,
                    ProviderResult.Fail(FailureKind.NotInstalled, "Claude Desktop metadata was not detected."));
        }
        catch (UnauthorizedAccessException)
        { return new(Client, ClaudeAuthentication.Unknown, null, ProviderResult.Fail(FailureKind.AccessDenied)); }
        catch (IOException)
        { return new(Client, ClaudeAuthentication.Unknown, null, ProviderResult.Fail(FailureKind.Unexpected)); }

        // Desktop can be signed in while Code is signed out. Its saved last-known
        // account/layout fields do not prove current authentication or organization.
        // Until a safe identity interface exists, no cache snapshot crosses this boundary.
        return new(Client, ClaudeAuthentication.Unknown, null, await ReadUsageAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<ProviderResult> ReadUsageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var paths = findCaches().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0)
                return ProviderResult.Fail(FailureKind.Unsupported,
                    "No Claude Desktop usage cache is available.");
            if (paths.Length > 1)
                return ProviderResult.Fail(FailureKind.Unsupported,
                    "Multiple Claude desktop installations have usage caches; the active account cannot be verified.");
            await using var file = new FileStream(paths[0], FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length > MaximumBytes) return ProviderResult.Fail(FailureKind.Malformed, "Claude desktop usage cache is too large.");
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await file.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaximumBytes)
                    return ProviderResult.Fail(FailureKind.Malformed, "Claude desktop usage cache is too large.");
                buffer.Write(chunk, 0, read);
            }
            var json = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            var result = ClaudeParser.Parse(json.TrimStart('\uFEFF'), utcNow());
            // A single historical organization is not proof of the current Desktop account.
            // Keep schema diagnostics, but never publish this unverified cache as usage.
            return result.Snapshot is null ? result : ProviderResult.Fail(FailureKind.Unsupported, UnverifiedAccountMessage);
        }
        catch (FileNotFoundException) { return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop usage cache is no longer available."); }
        catch (DirectoryNotFoundException) { return ProviderResult.Fail(FailureKind.Unsupported, "Claude desktop usage cache is no longer available."); }
        catch (UnauthorizedAccessException) { return ProviderResult.Fail(FailureKind.AccessDenied); }
        catch (DecoderFallbackException) { return ProviderResult.Fail(FailureKind.Malformed); }
        catch (IOException) { return ProviderResult.Fail(FailureKind.Unexpected, "Claude desktop usage cache could not be read."); }
    }

    public static IReadOnlyList<string> FindUsageCaches()
        => FindDataRoots().Select(root => Path.Combine(root, "plan-usage-history.json")).Where(File.Exists).ToArray();

    public static IReadOnlyList<string> FindDataRoots(string? roaming = null, string? local = null)
    {
        var paths = new List<string>();
        roaming ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        local ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (Path.IsPathFullyQualified(roaming))
        {
            var ordinary = Path.Combine(roaming, "Claude");
            if (Directory.Exists(ordinary)) paths.Add(ordinary);
        }
        if (!Path.IsPathFullyQualified(local)) return paths;
        var packages = Path.Combine(local, "Packages");
        if (!Directory.Exists(packages)) return paths;
        foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*", SearchOption.TopDirectoryOnly))
        {
            var candidate = Path.Combine(package, "LocalCache", "Roaming", "Claude");
            if (Directory.Exists(candidate)) paths.Add(candidate);
        }
        return paths;
    }
}
