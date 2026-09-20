using System.Text;

namespace AgentMeter.Core;

public sealed class DiagnosticLog(string directory)
{
    private readonly object gate = new();
    public bool WriteFailed { get; private set; }
    public void Write(string safeEvent)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "agentmeter.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, Path.Combine(directory, "agentmeter.previous.log"), true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {safeEvent}{Environment.NewLine}", Encoding.UTF8);
                WriteFailed = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { WriteFailed = true; } // Diagnostics cannot take down the tray; the popup exposes this condition.
        }
    }
}
