using Windows.ApplicationModel;

namespace AgentMeter;

internal interface IStartupTaskAccess
{
    StartupTaskState Read();
    StartupTaskState Enable();
    void Disable();
}

internal sealed class WindowsStartupTaskAccess : IStartupTaskAccess
{
    internal const string TaskId = "AgentMeterStartup";
    // Desktop StartupTask does not prompt. Keep the synchronous Settings contract;
    // execute WinRT completion off the UI context with a bounded deadline.
    private static T Run<T>(Func<Task<T>> action) => Task.Run(action).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    public StartupTaskState Read() => Run(async () => (await StartupTask.GetAsync(TaskId)).State);
    public StartupTaskState Enable() => Run(async () => await (await StartupTask.GetAsync(TaskId)).RequestEnableAsync());
    public void Disable() => Run(async () => { (await StartupTask.GetAsync(TaskId)).Disable(); return true; });
}

internal sealed class PackagedStartupRegistration(IStartupTaskAccess task, Action<string>? log = null) : IStartupRegistration
{
    public bool TryRead(out bool enabled)
    {
        enabled = false;
        try
        {
            var state = task.Read();
            enabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            // Never override the user's Task Manager/Windows Settings choice or policy.
            return state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        }
        catch (Exception e) when (Expected(e)) { log?.Invoke("startup.read-failed"); return false; }
    }
    public bool TrySet(bool enabled)
    {
        try
        {
            if (!TryRead(out var current)) return false;
            if (current == enabled) return true;
            if (enabled) return task.Enable() == StartupTaskState.Enabled;
            task.Disable(); return task.Read() == StartupTaskState.Disabled;
        }
        catch (Exception e) when (Expected(e)) { log?.Invoke("startup.write-failed"); return false; }
    }
    private static bool Expected(Exception e) => e is System.Runtime.InteropServices.COMException or
        UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.IO.IOException;
}
