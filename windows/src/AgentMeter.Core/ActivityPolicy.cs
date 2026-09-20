namespace AgentMeter.Core;

public sealed record SurfaceActivity(bool Cli = false, bool Desktop = false)
{
    public bool Active => Cli || Desktop;
}
public sealed record ActivitySnapshot(SurfaceActivity Codex, SurfaceActivity Claude)
{
    public static ActivitySnapshot Empty { get; } = new(new(), new());
    public string[] Providers => new[] { Codex.Active ? "Codex" : null, Claude.Active ? "Claude Code" : null }.OfType<string>().ToArray();
}

public static class ActivityPolicy
{
    private static readonly string[] CodexFlags = ["--no-alt-screen", "--strict-config", "--oss", "--approve-for-me", "--search", "--sandbox", "-s"];
    private static readonly string[] ClaudeFlags = ["--safe-mode", "--verbose", "--no-chrome", "--strict-mcp-config", "--disable-slash-commands", "--effort"];
    private static readonly string[] SandboxValues = ["read-only", "workspace-write", "danger-full-access"];
    private static readonly string[] EffortValues = ["low", "medium", "high", "xhigh", "max"];
    public static bool DesktopActive(bool foreground, bool visible, bool minimized) => foreground && visible && !minimized;

    // Streaming finite grammar: retain only matches to known literals, never arbitrary arguments.
    // Stop at the first unrecognized character rather than collecting a possible prompt.
    public static bool Interactive(string provider, Func<int, char?> read, string executable, int characters)
    {
        if (characters is < 1 or > 4096 || provider is not ("Codex" or "Claude Code")) return false;
        var aliases = new[] { executable, Path.GetFileName(executable), Path.GetFileNameWithoutExtension(executable) };
        var flags = provider == "Codex" ? CodexFlags : ClaudeFlags;
        var index = 0;
        string? Match(string[] choices, bool allowQuoted)
        {
            var quoted = allowQuoted && read(index) == '"';
            if (quoted) index++;
            var candidates = choices.ToList();
            var offset = 0;
            while (index < characters)
            {
                var c = read(index);
                if (c is null) return null;
                if ((quoted && c == '"') || (!quoted && c is ' ' or '\t')) break;
                candidates.RemoveAll(s => s.Length <= offset || char.ToUpperInvariant(s[offset]) != char.ToUpperInvariant(c.Value));
                if (candidates.Count == 0) return null;
                index++; offset++;
            }
            var result = candidates.FirstOrDefault(s => s.Length == offset);
            if (quoted) { if (index >= characters || read(index) != '"') return null; index++; }
            return result;
        }
        if (Match(aliases, true) is null) return false;
        var pending = false;
        for (var count = 0; index < characters && count < 16; count++)
        {
            if (read(index) is not (' ' or '\t')) return false;
            while (index < characters && read(index) is ' ' or '\t') index++;
            if (index == characters) return !pending;
            var choices = pending ? provider == "Codex" ? SandboxValues : EffortValues : flags;
            var value = Match(choices, false);
            if (value is null) return false;
            pending = !pending && value is "--sandbox" or "-s" or "--effort";
        }
        return index == characters && !pending;
    }

    public static bool Interactive(string provider, string command, string executable) =>
        Interactive(provider, i => i < command.Length ? command[i] : null, executable, command.Length);
}
