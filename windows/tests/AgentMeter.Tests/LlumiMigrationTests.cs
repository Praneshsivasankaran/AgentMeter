namespace AgentMeter.Tests;

public sealed class LlumiMigrationTests
{
    [Fact]
    public void MigrationIsAllowlistedTypedAndIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var old = Path.Combine(root,"old"); var current=Path.Combine(root,"current");
        try
        {
            Directory.CreateDirectory(old); Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(old,"v2-preferences.json"), "{\"Appearance\":2,\"CompactMonitor\":false,\"TrayIcon\":\"false\",\"token\":\"synthetic\"}");
            File.WriteAllText(Path.Combine(old,"setup-completed.json"), "true");
            File.WriteAllText(Path.Combine(current,"v2-preferences.json"), "{\"Appearance\":1}");
            LegacyPreferences.Migrate(old,current);
            var preferences=new PreferenceStore(Path.Combine(current,"v2-preferences.json")).Load();
            Assert.Equal(Appearance.Light,preferences.Appearance);Assert.False(preferences.CompactMonitor);Assert.True(preferences.TrayIcon);
            Assert.True(new SetupCompletionStore(Path.Combine(current,"setup-completed.json")).IsComplete());
            Assert.DoesNotContain("token",File.ReadAllText(Path.Combine(current,"v2-preferences.json")));
            File.WriteAllText(Path.Combine(current,"v2-preferences.json"),"{\"Appearance\":0}");
            LegacyPreferences.Migrate(old,current);
            Assert.Equal(Appearance.System,new PreferenceStore(Path.Combine(current,"v2-preferences.json")).Load().Appearance);
            Assert.True(File.Exists(Path.Combine(old,"v2-preferences.json")));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public void InvalidAndDuplicateLegacyValuesDoNotCompleteSetup()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));var old=Path.Combine(root,"old");var current=Path.Combine(root,"new");
        try {
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old,"v2-preferences.json"),"{\"Appearance\":1,\"Appearance\":2}");
            File.WriteAllText(Path.Combine(old,"setup-completed.json"),"\"true\"");
            LegacyPreferences.Migrate(old,current);
            Assert.False(new SetupCompletionStore(Path.Combine(current,"setup-completed.json")).IsComplete());
            Assert.False(File.Exists(Path.Combine(current,"v2-preferences.json")));
        } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public void DisplayProductAndStartupUseLlumiWhilePackageTaskRemainsCompatible()
    {
        var product=System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyProductAttribute>(typeof(AppIcon).Assembly);
        Assert.Equal("Llumi",product?.Product);
        Assert.Equal("Llumi",StartupRegistration.ValueName);
        Assert.Equal("AgentMeterStartup",WindowsStartupTaskAccess.TaskId);
    }
}
