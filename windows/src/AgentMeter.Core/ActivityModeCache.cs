namespace AgentMeter.Core;

// A process may be observed before its console/parameters are initialized.
// Only a verified interactive result is sticky. Unavailable/negative reads are
// retried on the next ordinary one-second activity sample, never guessed active.
internal sealed class ActivityModeCache
{
    private readonly HashSet<(int Pid, long Created)> interactive = [];
    internal bool Read((int Pid, long Created) key, Func<bool> probe)
    {
        if (interactive.Contains(key)) return true;
        if (!probe()) return false;
        interactive.Add(key); return true;
    }
    internal void Retain(HashSet<(int, long)> seen) => interactive.IntersectWith(seen);
}
