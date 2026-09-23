using System.Runtime.InteropServices;

namespace AgentMeter.Core;

public sealed record CliSearchEnvironment(
    string? PathValue,
    string LocalApplicationData,
    string RoamingApplicationData,
    Architecture Architecture,
    string? CodexOverride = null)
{
    public static CliSearchEnvironment Current() => new(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        RuntimeInformation.OSArchitecture,
        Environment.GetEnvironmentVariable("LLUMI_CODEX_PATH")
            ?? Environment.GetEnvironmentVariable("AGENTMETER_CODEX_PATH"));
}

public static class CliLocator
{
    public static string? FindCodex() => FindCodex(CliSearchEnvironment.Current());

    public static string? FindCodex(CliSearchEnvironment environment)
    {
        // An explicit override is authoritative, including an invalid path.
        if (environment.CodexOverride is not null) return ExistingExecutable(environment.CodexOverride);
        var pathDirectories = PathDirectories(environment.PathValue).ToArray();
        var onPath = FindInDirectories("codex.exe", pathDirectories);
        if (onPath is not null) return onPath;

        // npm's Windows shim is a .cmd. Resolve its native binary, never invoke a shell.
        foreach (var prefix in pathDirectories)
        {
            var npmExecutable = FindNpmCodex(prefix, environment.Architecture);
            if (npmExecutable is not null) return npmExecutable;
        }

        if (Path.IsPathFullyQualified(environment.LocalApplicationData))
        {
            var versions = Path.Combine(environment.LocalApplicationData, "OpenAI", "Codex", "bin");
            try
            {
                if (Directory.Exists(versions))
                    foreach (var directory in new DirectoryInfo(versions).EnumerateDirectories()
                        .OrderByDescending(d => d.LastWriteTimeUtc).ThenBy(d => d.Name, StringComparer.Ordinal))
                    {
                        var executable = ExistingExecutable(Path.Combine(directory.FullName, "codex.exe"));
                        if (executable is not null) return executable;
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return Path.IsPathFullyQualified(environment.RoamingApplicationData)
            ? FindNpmCodex(Path.Combine(environment.RoamingApplicationData, "npm"), environment.Architecture)
            : null;
    }

    private static string? FindNpmCodex(string prefix, Architecture architecture)
    {
        var (packageName, target) = architecture switch
        {
            Architecture.Arm64 => ("codex-win32-arm64", "aarch64-pc-windows-msvc"),
            Architecture.X64 => ("codex-win32-x64", "x86_64-pc-windows-msvc"),
            _ => ("", "")
        };
        if (target.Length == 0) return null;
        var openai = Path.Combine(prefix, "node_modules", "@openai");
        foreach (var package in new[] { "codex", packageName })
        {
            var relative = Path.Combine(package, "vendor", target, "codex", "codex.exe");
            var executable = ExistingExecutable(Path.Combine(openai, relative));
            if (executable is not null) return executable;
            executable = ExistingExecutable(Path.Combine(openai, "codex", "node_modules", "@openai", relative));
            if (executable is not null) return executable;
        }
        return null;
    }

    public static string? FindOnPath(string executableName) =>
        FindOnPath(executableName, Environment.GetEnvironmentVariable("PATH"));

    public static string? FindOnPath(string executableName, string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(executableName) || Path.GetFileName(executableName) != executableName) return null;
        return FindInDirectories(executableName, PathDirectories(pathValue));
    }

    private static string? FindInDirectories(string executableName, IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            var executable = ExistingExecutable(Path.Combine(directory, executableName));
            if (executable is not null) return executable;
        }
        return null;
    }

    private static IEnumerable<string> PathDirectories(string? pathValue)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (pathValue ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            string? directory = null;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                // Relative PATH entries must never make discovery depend on the working directory.
                if (Path.IsPathFullyQualified(expanded)) directory = Path.GetFullPath(expanded);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }
            if (directory is not null && seen.Add(directory)) yield return directory;
        }
    }

    public static string? ExistingExecutable(string path)
    {
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.IsPathFullyQualified(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? Path.GetFullPath(path) : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
