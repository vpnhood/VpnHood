using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Checks;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management.EndPoints;

// The list's update from its URL: a job whose first download comes at once and the next ones every interval, each
// merged into the store, loaded again and checked; and a connection's wait for the list while no endpoint is enabled
internal sealed class ProxyAutoUpdater(
    ProxyEndPointList list,
    IProxyEndPointStore store,
    ConnectCheckRunner checkRunner,
    ProxyConnectorClose close,
    ProxyAutoUpdateOptions options)
{
    // a connect waits for a job's first download, so a list's host that does not answer may not hold it for long
    private static readonly TimeSpan FirstListDownloadTimeout = TimeSpan.FromSeconds(30);

    // the job of the list's update, made, replaced and closed under its lock
    private readonly Lock _autoUpdateLock = new();
    private Job? _autoUpdateJob;
    // the job's first download, which a connection waits for while no endpoint is enabled; under the job's lock too
    private TaskCompletionSource? _firstListUpdate;
    // the connections waiting for the list's endpoints, which check what a reload brings themselves
    private int _endPointWaiterCount;

    // one run of the update at a time, which the close waits for before it closes the store
    public AsyncLock UpdateLock { get; } = new();

    public ProxyAutoUpdateOptions Options { get; private set; } = options;

    // the connections waiting for the list's endpoints; a test's to look at
    public int EndPointWaiterCount => Volatile.Read(ref _endPointWaiterCount);

    // whether a connection waits for the list, which checks what a reload brings itself
    public bool IsWaitedFor => Volatile.Read(ref _endPointWaiterCount) > 0;

    // Restarts the job for a new URL or interval alone, as an app sends the options at every save; for the same list,
    // where the reload took the last enabled endpoint, or left none and no job, as after a list's one download, unless
    // its first download is still coming, which a new job would only start again
    public void ApplyOptions(ProxyAutoUpdateOptions newOptions, bool hadEnabledEndPoint)
    {
        bool isFirstDownloadPending;
        lock (_autoUpdateLock)
            isFirstDownloadPending = _firstListUpdate is { Task.IsCompleted: false };

        var oldAutoUpdateOptions = Options;
        Options = newOptions;
        if (Options.Url != oldAutoUpdateOptions.Url ||
            Options.Interval != oldAutoUpdateOptions.Interval ||
            (Options.Url != null && !list.HasEnabledEndPoint && !isFirstDownloadPending &&
             (hadEnabledEndPoint || _autoUpdateJob == null)))
            Restart(firstRunSocketFactory: null);
    }

    // At the close, under the job's lock, so new options cannot put a job in place after it; out of the field first, so
    // its first run's end does not start it
    public void Stop()
    {
        lock (_autoUpdateLock) {
            var autoUpdateJob = _autoUpdateJob;
            _autoUpdateJob = null;
            autoUpdateJob?.Dispose();
            _firstListUpdate?.TrySetResult(); // its waiters go on, to find the connector closed
        }
    }

    // A first download at once, not at the job runner's next check up to 10 seconds away, as a connection may wait for
    // it; then one every interval, or with none, the app's "never", only while no endpoint is enabled. The new job is
    // in place before the old one stops, so the old one's waiters find it. Only Create's gives a factory: the tunnel is
    // not up yet
    public void Restart(ISocketFactory? firstRunSocketFactory)
    {
        lock (_autoUpdateLock) {
            if (close.IsClosed)
                return;

            var oldAutoUpdateJob = _autoUpdateJob;
            var oldFirstListUpdate = _firstListUpdate;
            var interval = Options.Interval is { } value && value > TimeSpan.Zero ? value : (TimeSpan?)null;
            if (Options.Url is not { } url || (interval == null && list.HasEnabledEndPoint)) {
                _autoUpdateJob = null;
                _firstListUpdate = null;
            }
            else {
                // a job's runs come one at a time, the first by RunFirstListUpdate before the job starts. Its URL is
                // its own: an old job's run under way when new options come takes none of theirs
                var runCount = 0;
                var firstListUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var autoUpdateJob = new Job(
                    cancellationToken => UpdateFromUrlAsync(url, isFirstRun: ++runCount == 1, firstRunSocketFactory,
                        firstListUpdate, cancellationToken),

                    new JobOptions {
                        Interval = interval ?? TimeSpan.MaxValue,
                        DueTime = interval ?? TimeSpan.MaxValue,
                        Name = "ProxyEndPointAutoUpdate",
                        AutoStart = false
                    });

                _autoUpdateJob = autoUpdateJob;
                _firstListUpdate = firstListUpdate;

                // off the caller's thread: an HTTP client's start can block there, as on a system proxy's detection
                _ = Task.Run(() => RunFirstListUpdate(autoUpdateJob, firstListUpdate, isPeriodic: interval != null));
            }

            oldAutoUpdateJob?.Dispose();
            oldFirstListUpdate?.TrySetResult();
        }
    }

    // The job runs once now, and its waiters go on at the run's end; a periodic one then starts, an interval from that
    // run's end, unless it was replaced or closed
    private async Task RunFirstListUpdate(Job autoUpdateJob, TaskCompletionSource firstListUpdate, bool isPeriodic)
    {
        try {
            await autoUpdateJob.RunNow().Vhc();
        }
        catch (Exception ex) {
            // a job replaced or closed before its run
            VhLogger.Instance.LogDebug(ex, "The proxy list's first download did not run.");
        }
        finally {
            firstListUpdate.TrySetResult();
        }

        // the close and a restart take the job out of the field, under this lock, before they stop it
        lock (_autoUpdateLock) {
            if (isPeriodic && _autoUpdateJob == autoUpdateJob)
                autoUpdateJob.Start();
        }
    }

    // The first download is over at the merge, as new options would otherwise find it still coming and start no other
    // where the user turned off what it brought. A first run leaves the check to its connect, or, where that one ended,
    // checks those never rated; a later run the whole list, unless a check is under way, which takes what it brought
    private async ValueTask UpdateFromUrlAsync(Uri url, bool isFirstRun, ISocketFactory? firstRunSocketFactory,
        TaskCompletionSource firstListUpdate, CancellationToken cancellationToken)
    {
        try {
            using var listUpdateScope = await UpdateLock.LockAsync(cancellationToken).Vhc();
            VhLogger.Instance.LogInformation("Downloading proxy list from {Url}...", url);

            // the first run, which a connect may wait for, over its factory for 30 seconds at most; the later ones the
            // platform's way, with HttpClient's time. A new client each time, as the downloads are far apart
            using var httpClient = isFirstRun
                ? ProxyListHttpClient.Create(firstRunSocketFactory, FirstListDownloadTimeout)
                : ProxyListHttpClient.Create(socketFactory: null, timeout: null);

            var newEndPoints = await ProxyEndPointUpdater.LoadFromUrlAsync(httpClient, url, cancellationToken).Vhc();
            if (newEndPoints.Length == 0) {
                VhLogger.Instance.LogWarning("No proxies found in downloaded content from {Url}", url);
                return;
            }

            // merge into the shared store and reload the working set with preserved statuses, with no flush between
            IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore;
            bool isWaitedFor;
            using (await list.FlushLock.LockAsync(cancellationToken).Vhc()) {
                enabledBefore = list.GetEnabledAddresses();
                isWaitedFor = Volatile.Read(ref _endPointWaiterCount) > 0;
                await list.Flush(cancellationToken).Vhc();
                await store.Merge(newEndPoints, Options.MaxItemCount, Options.MaxPenalty,
                    Options.RemoveDuplicateIps, cancellationToken).Vhc();

                await list.Reload(cancellationToken).Vhc();
                firstListUpdate.TrySetResult();
            }

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                list.Entries.Length);

            if (isFirstRun)
                checkRunner.StartAddedCheck(enabledBefore, isWaitedFor);
            else
                checkRunner.StartListCheck();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // a restart or the close
        }
        catch (ObjectDisposedException) when (close.IsClosed) {
            // the close, during the merge
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", url);
        }
    }

    // the connector's WaitForEndPoints
    public async Task<bool> WaitForEndPoints(CancellationToken cancellationToken)
    {
        close.ThrowIfClosed();
        bool hasEnabledEndPoint;
        while (true) {
            // the reload's signal before the look at the endpoints: a reload after the look completes this one
            var entriesReload = list.EntriesReload;
            hasEnabledEndPoint = list.HasEnabledEndPoint;
            if (hasEnabledEndPoint)
                break;

            // the download to wait for, read under the lock a restart takes, which puts the new one in place as it
            // completes the old one
            TaskCompletionSource? firstListUpdate;
            lock (_autoUpdateLock)
                firstListUpdate = _firstListUpdate is { Task.IsCompleted: false } current ? current : null;

            // with none coming, the answer is the look, unless a reload came since it, as the download's own merge
            if (firstListUpdate == null) {
                if (entriesReload.IsCompleted)
                    continue;

                break;
            }

            // counted while it waits, so a reload that brings endpoints leaves their check to it
            Interlocked.Increment(ref _endPointWaiterCount);
            try {
                await Task.WhenAny(firstListUpdate.Task, entriesReload).WaitAsync(cancellationToken).Vhc();
            }
            finally {
                Interlocked.Decrement(ref _endPointWaiterCount);
            }
        }

        // a cancellation is the caller's, never an answer that no endpoint is there. The answer is the look that ended
        // the wait: a second look could find a reload's change while a download is still coming
        cancellationToken.ThrowIfCancellationRequested();
        close.ThrowIfClosed();
        return hasEnabledEndPoint;
    }
}
