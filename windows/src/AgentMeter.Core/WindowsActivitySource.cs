using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentMeter.Core;

/// <summary>Metadata only. No window text, accessibility, terminal content, or screen capture.</summary>
public sealed class WindowsActivitySource
{
    private readonly ActivityModeCache modes = new();
    private string? codex, claude;
    private DateTime nextDiscovery;
    public ActivitySnapshot Capture()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return ActivitySnapshot.Empty;
        if (DateTime.UtcNow >= nextDiscovery)
        { codex = CliLocator.FindCodex(); claude = ClaudeCliLocator.Find(); nextDiscovery = DateTime.UtcNow.AddSeconds(30); }
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var frontPid);
        var desktopAllowed = ActivityPolicy.DesktopActive(true, IsWindowVisible(foreground), IsIconic(foreground));
        var cxCli = false; var clCli = false; var cxDesktop = false; var clDesktop = false;
        var seen = new HashSet<(int, long)>();
        // Process names and paths are metadata; do not request WMI command lines.
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || ProviderProcess.IsOwned(process.Id)) continue;
                    var name = process.ProcessName;
                    if (!name.Equals("codex", StringComparison.OrdinalIgnoreCase) && !name.Equals("claude", StringComparison.OrdinalIgnoreCase) &&
                        !(process.Id == frontPid && name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))) continue;
                    using var handle = OpenProcess(0x1000 | 0x10, false, process.Id);
                    if (handle.IsInvalid) continue;
                    var buffer = new StringBuilder(32768); var capacity = buffer.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, buffer, ref capacity)) continue;
                    var path = buffer.ToString();
                    if (process.Id == frontPid && desktopAllowed)
                    {
                        cxDesktop |= DesktopProvider(path) == "Codex";
                        clDesktop |= DesktopProvider(path) == "Claude Code";
                    }
                    var provider = Same(path, codex) ? "Codex" : Same(path, claude) ? "Claude Code" : null;
                    if (provider is null) continue;
                    var key = (process.Id, process.StartTime.ToUniversalTime().Ticks);
                    seen.Add(key);
                    var active = modes.Read(key, () => ReadMode(handle, provider, path));
                    if (provider == "Codex") cxCli |= active; else clCli |= active;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        modes.Retain(seen);
        return new(new(cxCli, cxDesktop), new(clCli, clDesktop));
    }

    public static string? DesktopProvider(string path)
    {
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(apps, StringComparison.OrdinalIgnoreCase)) return null;
        var relative = path[apps.Length..].Split(Path.DirectorySeparatorChar);
        if (relative.Length != 3 || !relative[1].Equals("app", StringComparison.OrdinalIgnoreCase)) return null;
        if (relative[0].StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) && relative[0].EndsWith("__2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase) &&
            relative[2].Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return "Codex";
        if (relative[0].StartsWith("Claude_", StringComparison.OrdinalIgnoreCase) && relative[0].EndsWith("__pzs8sxrjxfjjc", StringComparison.OrdinalIgnoreCase) &&
            relative[2].Equals("Claude.exe", StringComparison.OrdinalIgnoreCase)) return "Claude Code";
        return null;
    }
    private static bool Same(string path, string? other) => other is not null && path.Equals(other, StringComparison.OrdinalIgnoreCase);
    private static bool ReadMode(SafeProcessHandle process, string provider, string executable)
    {
        if (!IsWow64Process(process, out var wow64) || wow64) return false;
        var basic = new byte[48];
        if (NtQueryInformationProcess(process, 0, basic, basic.Length, out _) != 0) return false;
        var peb = BitConverter.ToInt64(basic, 8);
        byte[]? Read(long address, int length)
        {
            if (address <= 0) return null;
            var data = new byte[length];
            return ReadProcessMemory(process, new IntPtr(address), data, (nuint)length, out var received) && received == (nuint)length ? data : null;
        }
        // Documented x64 PEB and RTL_USER_PROCESS_PARAMETERS layout; unsupported layouts fail closed.
        var parameters = Read(peb + 32, 8);
        if (parameters is null) return false;
        var parameterAddress = BitConverter.ToInt64(parameters);
        var console = Read(parameterAddress + 16, 8);
        // Interactive consoles and ConPTY have a console handle. Detached helpers do not.
        if (console is null || BitConverter.ToInt64(console) is 0 or -1) return false;
        var command = Read(parameterAddress + 112, 16);
        if (command is null) return false;
        var length = BitConverter.ToUInt16(command, 0);
        if (length % 2 != 0 || length is 0 or > 8192) return false;
        var pointer = BitConverter.ToInt64(command, 8);
        char? Character(int i)
        { var data = Read(pointer + i * 2L, 2); return data is null ? null : (char)BitConverter.ToUInt16(data); }
        return ActivityPolicy.Interactive(provider, Character, executable, length / 2);
    }
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint size, out nuint received);
    [DllImport("kernel32.dll")] private static extern bool IsWow64Process(SafeProcessHandle process, out bool wow64);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle process, int kind, byte[] buffer, int length, out int returned);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
}
