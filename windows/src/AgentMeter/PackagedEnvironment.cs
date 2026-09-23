using System.Runtime.InteropServices;
using Windows.Storage;

namespace AgentMeter;

internal static class PackagedEnvironment
{
    internal static bool HasIdentity
    {
        get { uint length = 0; return GetCurrentPackageFullName(ref length, 0) == 122; }
    }
    // Explicit package-owned LocalState avoids writing through MSIX's merged
    // AppData view into existing portable preferences. Windows manages its lifecycle.
    internal static string DataDirectory => HasIdentity
        ? ApplicationData.Current.LocalFolder.Path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Llumi");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, nint name);
}
