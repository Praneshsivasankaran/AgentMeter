using System.Security;
using Microsoft.Win32;

namespace AgentMeter;

internal interface IStartupRegistration
{
    bool TryRead(out bool enabled);
    bool TrySet(bool enabled);
}

// The installer and the app share this one per-user entry. It is the persisted setting;
// mirroring it into JSON could re-enable startup after a user has disabled it elsewhere.
internal sealed class StartupRegistration(string executable, Action<string>? log = null,
    string keyPath = StartupRegistration.RunKey) : IStartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "AgentMeter";

    internal static string Command(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || executable.Any(char.IsControl) ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Startup requires an absolute executable path.", nameof(executable));
        var command = $"\"{Path.GetFullPath(executable)}\" --startup";
        // Windows documents a 260-character maximum for Run/RunOnce data.
        if (command.Length > 260)
            throw new ArgumentException("Startup command exceeds the Windows Run limit.", nameof(executable));
        return command;
    }

    public bool TryRead(out bool enabled)
    {
        enabled = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            enabled = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value &&
                string.Equals(value, Command(executable), StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        { log?.Invoke("startup.read-failed"); return false; }
    }

    public bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            log?.Invoke(enabled ? "startup.enabled" : "startup.disabled");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        { log?.Invoke("startup.write-failed"); return false; }
    }
}
