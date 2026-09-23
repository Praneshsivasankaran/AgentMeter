using System.Diagnostics;

namespace AgentMeter.Core;

/// <summary>An owned empty directory, never the application checkout or user home.</summary>
public sealed class ProviderWorkspace : IDisposable
{
    public string DirectoryPath { get; }
    public ProviderWorkspace()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Llumi", "ProviderWork"));
        for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Provider workspace cannot use redirected directories.");
        Directory.CreateDirectory(root);
        DirectoryPath = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public void Configure(ProcessStartInfo info)
    {
        info.WorkingDirectory = DirectoryPath;
        // Preserve provider-owned auth environment; prevent inherited Git worktree routing.
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(key);
        info.Environment["PWD"] = DirectoryPath;
        info.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(DirectoryPath);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
    }

    public void Dispose()
    {
        try
        {
            // Never follow/recurse through provider-created content or links during cleanup.
            if (Directory.Exists(DirectoryPath) && !File.GetAttributes(DirectoryPath).HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(DirectoryPath, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
