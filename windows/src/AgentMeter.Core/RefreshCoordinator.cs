namespace AgentMeter.Core;

public sealed class RefreshCoordinator
{
    private readonly IUsageProvider[] providers;
    private readonly Dictionary<string, ProviderState> states;
    private readonly Dictionary<string, Task<ProviderResult>> pending = new();
    private readonly Dictionary<string, long> lastStarts = new();
    private readonly HashSet<string> active = new();
    private readonly object stateLock = new();
    private readonly TimeProvider clock;
    private readonly TimeSpan timeout;
    private readonly TimeSpan minimumInterval;
    private readonly Action<string> log;
    public event Action? Changed;
    public bool IsRefreshing { get { lock (stateLock) return active.Count != 0; } }

    public RefreshCoordinator(IEnumerable<IUsageProvider> providers, Action<string>? log = null,
        TimeProvider? clock = null, TimeSpan? timeout = null, TimeSpan? minimumInterval = null)
    {
        this.providers = providers.ToArray();
        states = this.providers.ToDictionary(p => p.Name, p => new ProviderState(p.Name, ProviderStatus.Loading));
        this.log = log ?? (_ => { });
        this.clock = clock ?? TimeProvider.System;
        this.timeout = timeout ?? TimeSpan.FromSeconds(25);
        this.minimumInterval = minimumInterval ?? TimeSpan.FromSeconds(10);
        if (this.timeout <= TimeSpan.Zero || this.timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (this.minimumInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumInterval));
    }

    public IReadOnlyList<ProviderState> States
    {
        get { lock (stateLock) return providers.Select(p => states[p.Name]).ToArray(); }
    }

    // Call after canceling the application lifetime and stopping refresh scheduling.
    // Canceled provider tasks still need a moment to dispose their owned process jobs.
    public async Task DrainAsync()
    {
        Task[] operations;
        lock (stateLock) operations = pending.Values.Cast<Task>().ToArray();
        try { await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
        catch (Exception) { /* Shutdown is bounded even for an uncooperative adapter. */ }
    }

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = new List<(IUsageProvider Provider, ProviderState Previous)>();
        lock (stateLock)
        {
            // Reserve each provider independently. A slow query cannot suppress a healthy
            // provider's next refresh, and even an uncooperative timed-out task cannot overlap.
            var now = clock.GetTimestamp();
            foreach (var provider in providers)
            {
                var name = provider.Name;
                if (active.Contains(name) || pending.TryGetValue(name, out var operation) && !operation.IsCompleted ||
                    lastStarts.TryGetValue(name, out var last) && clock.GetElapsedTime(last, now) < minimumInterval)
                    continue;
                active.Add(name);
                lastStarts[name] = now;
                selected.Add((provider, states[name]));
                states[name] = states[name] with { Status = ProviderStatus.Loading };
            }
        }
        if (selected.Count == 0) return false;
        log("refresh.started");
        Changed?.Invoke();
        await Task.WhenAll(selected.Select(item => RefreshProviderAsync(item.Provider, item.Previous, cancellationToken))).ConfigureAwait(false);
        log("refresh.completed");
        return true;
    }

    private async Task RefreshProviderAsync(IUsageProvider provider, ProviderState previous, CancellationToken cancellationToken)
    {
        try
        {
            ProviderResult result;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                var operation = Task.Run(() => provider.QueryAsync(deadline.Token), deadline.Token);
                lock (stateLock) pending[provider.Name] = operation;
                _ = operation.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                result = await operation.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { result = ProviderResult.Fail(FailureKind.Timeout); }
            catch (OperationCanceledException)
            {
                lock (stateLock) states[provider.Name] = previous;
                return;
            }
            catch (UnauthorizedAccessException) { result = ProviderResult.Fail(FailureKind.AccessDenied); }
            catch (Exception) { result = ProviderResult.Fail(FailureKind.Unexpected); }

            // Guard the adapter boundary as well as provider-specific parsers.
            if (result.Failure == FailureKind.None && (result.Snapshot is not { } snapshot ||
                snapshot.Windows.Count == 0 || snapshot.ObservedAt > clock.GetUtcNow().AddMinutes(1)))
                result = ProviderResult.Fail(FailureKind.Malformed);
            var success = result.Snapshot is not null && result.Failure == FailureKind.None;
            lock (stateLock)
            {
                var retained = result.Failure is FailureKind.NotInstalled or FailureKind.LoggedOut or FailureKind.Unsupported
                    ? null : previous.Snapshot;
                states[provider.Name] = new(provider.Name,
                    success ? ProviderStatus.Ready : result.Failure is FailureKind.NotInstalled or FailureKind.LoggedOut or FailureKind.Unsupported
                        ? ProviderStatus.Unavailable : ProviderStatus.Error,
                    success ? result.Snapshot : retained, result.Failure, result.Detail, clock.GetUtcNow());
            }
            log($"provider.{provider.Name}.{(success ? "succeeded" : "failed")}.{result.Failure}");
        }
        finally
        {
            lock (stateLock) active.Remove(provider.Name);
            Changed?.Invoke();
        }
    }
}
