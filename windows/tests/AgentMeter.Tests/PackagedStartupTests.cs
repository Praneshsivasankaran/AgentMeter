using Windows.ApplicationModel;
namespace AgentMeter.Tests;
public sealed class PackagedStartupTests
{
    [Theory]
    [InlineData(StartupTaskState.Disabled, false, true)]
    [InlineData(StartupTaskState.Enabled, true, true)]
    [InlineData(StartupTaskState.DisabledByUser, false, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false, false)]
    [InlineData(StartupTaskState.EnabledByPolicy, true, false)]
    public void WindowsIsAuthoritativeAndExternalRestrictionsAreNotOverridden(StartupTaskState state, bool enabled, bool writable)
    {
        var access = new Fake(state); var registration = new PackagedStartupRegistration(access);
        Assert.Equal(writable, registration.TryRead(out var actual)); Assert.Equal(enabled, actual);
        Assert.Equal(writable, registration.TrySet(!enabled));
        Assert.Equal(writable ? 1 : 0, access.Writes);
    }
    [Fact]
    public void DeniedEnableNeverClaimsSuccess()
    {
        var access = new Fake(StartupTaskState.Disabled) { Deny = true };
        Assert.False(new PackagedStartupRegistration(access).TrySet(true));
        Assert.Equal(StartupTaskState.DisabledByUser, access.State);
    }
    [Fact]
    public void UnpackagedEnvironmentKeepsOriginalDataLocation()
    {
        Assert.False(PackagedEnvironment.HasIdentity);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Llumi"), PackagedEnvironment.DataDirectory);
    }
    private sealed class Fake(StartupTaskState state) : IStartupTaskAccess
    {
        internal StartupTaskState State = state; internal int Writes; internal bool Deny;
        public StartupTaskState Read() => State;
        public StartupTaskState Enable() { Writes++; return State = Deny ? StartupTaskState.DisabledByUser : StartupTaskState.Enabled; }
        public void Disable() { Writes++; State = StartupTaskState.Disabled; }
    }
}
