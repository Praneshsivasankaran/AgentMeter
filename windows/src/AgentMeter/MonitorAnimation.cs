using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentMeter;

// Windows Animation Manager supplies the easing. The owner pumps it only during
// a finite transition, then stops its WinForms timer completely (no idle frames).
internal sealed class MonitorAnimation : IDisposable
{
    private IManager? manager;
    private ILibrary? library;
    private IVariable? variable;
    private readonly Stopwatch clock = new();
    private double duration;
    internal bool Native { get; private set; }
    internal static bool MotionAllowed => !SystemInformation.HighContrast &&
        SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out var enabled, 0) && enabled;

    internal void Start(double seconds)
    {
        Stop(); duration = seconds;
        try
        {
            manager ??= (IManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new("4C1FC63A-695C-47E8-A339-1A194BE3D0B8"), true)!)!;
            library ??= (ILibrary)Activator.CreateInstance(Type.GetTypeFromCLSID(new("1D6322AD-AA85-4EF5-A828-86D71067D145"), true)!)!;
            manager.CreateAnimationVariable(0, out variable);
            library.CreateAccelerateDecelerateTransition(seconds, 1, 0, 1, out var transition);
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            try { manager.ScheduleTransition(variable, transition, now); manager.Update(now, out _); }
            finally { Marshal.Release(transition); }
            Native = true; clock.Restart();
        }
        catch (COMException) { Stop(); Native = false; }
    }
    internal double Progress
    {
        get
        {
            if (!Native || clock.Elapsed.TotalSeconds >= duration) return 1;
            try { manager!.Update(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, out _); variable!.GetValue(out var value); return Math.Clamp(value, 0, 1); }
            catch (COMException) { Stop(); return 1; }
        }
    }
    internal void Stop()
    {
        clock.Stop();
        if (manager is not null) manager.AbandonAllStoryboards();
        if (variable is not null) { Marshal.FinalReleaseComObject(variable); variable = null; }
    }
    public void Dispose()
    {
        Stop();
        if (library is not null) { Marshal.FinalReleaseComObject(library); library = null; }
        if (manager is not null) { Marshal.FinalReleaseComObject(manager); manager = null; }
    }

    // Prefixes of the documented UIAnimation.h vtables; unused slots are retained.
    [ComImport, Guid("9169896C-AC8D-4E7D-94E5-67FA4DC2F2E8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IManager
    {
        void CreateAnimationVariable(double value, out IVariable variable);
        void ScheduleTransition(IVariable variable, nint transition, double time);
        void CreateStoryboard(out nint storyboard);
        void FinishAllStoryboards(double deadline);
        void AbandonAllStoryboards();
        void Update(double time, out int result);
    }
    [ComImport, Guid("8CEEB155-2849-4CE5-9448-91FF70E1E4D9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVariable { void GetValue(out double value); }
    [ComImport, Guid("CA5A14B1-D24F-48B8-8FE4-C78169BA954E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILibrary
    {
        void CreateInstantaneousTransition(double value, out nint transition);
        void CreateConstantTransition(double duration, out nint transition);
        void CreateDiscreteTransition(double delay, double value, double hold, out nint transition);
        void CreateLinearTransition(double duration, double value, out nint transition);
        void CreateLinearTransitionFromSpeed(double speed, double value, out nint transition);
        void CreateSinusoidalTransitionFromVelocity(double duration, double period, out nint transition);
        void CreateSinusoidalTransitionFromRange(double duration, double min, double max, double period, int slope, out nint transition);
        void CreateAccelerateDecelerateTransition(double duration, double value, double acceleration, double deceleration, out nint transition);
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
