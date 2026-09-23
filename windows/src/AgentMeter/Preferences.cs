using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMeter;

internal enum Appearance { System, Light, Dark }
internal sealed record Preferences(bool CompactMonitor = true, bool TrayIcon = true, Appearance Appearance = Appearance.System);
internal sealed class PreferenceStore(string path)
{
    internal static PreferenceStore Default() => new(Path.Combine(PackagedEnvironment.DataDirectory, "v2-preferences.json"));
    internal bool HasValidExistingPreferences()
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 4096) return false;
            using var document = JsonDocument.Parse(file);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            return root.EnumerateObject().Any(p =>
                (p.Name is "CompactMonitor" or "TrayIcon" && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                || (p.Name == "Appearance" && p.Value.TryGetInt32(out var n) && Enum.IsDefined((Appearance)n)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }
    internal Preferences Load()
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 4096) return new();
            var value = JsonSerializer.Deserialize<Preferences>(file);
            return value is not null && Enum.IsDefined(value.Appearance) ? value : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal bool Save(Preferences value)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = File.Create(temporary)) { JsonSerializer.Serialize(output, value); output.Flush(true); }
            File.Move(temporary, path, true); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
