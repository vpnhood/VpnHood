using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management.EndPoints;

// The endpoints the connector holds, as last loaded from the store, which a reload makes anew: their save to the store,
// their lookups, the orders the checks and the connections take them in, and the record of each outcome
internal sealed class ProxyEndPointList(IProxyEndPointStore store)
{
    private readonly Lock _duplicateLock = new();
    private readonly DateTime _sessionCreatedTime = FastDateTime.UtcNow;
    private ProxyEndPointEntry[] _entries = [];
    private long _queuePosition;
    // completed, and replaced, at each reload of the entries, which may bring one a waiting connection can use
    private TaskCompletionSource _entriesReload = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // the statuses' resets by new options, which drop the successes the handed-out connections counted
    private long _statusResetCount;

    // one save at a time, and none between a save and the reload after it, which their callers take
    public AsyncLock FlushLock { get; } = new();

    // as last loaded: a reload puts a new array in place
    public ProxyEndPointEntry[] Entries => _entries;

    // while the store has any endpoint, as last loaded
    public bool IsEnabled { get; private set; }

    public bool HasEnabledEndPoint => _entries.Any(x => x.EndPoint.IsEnabled);

    // the next reload's signal, which a waiting connection takes before it looks at the endpoints
    public Task EntriesReload => Volatile.Read(ref _entriesReload).Task;

    public long StatusResetCount => Interlocked.Read(ref _statusResetCount);

    public void CountStatusReset() => Interlocked.Increment(ref _statusResetCount);

    // a connection's turn, from which each endpoint's next turn counts
    public void AdvanceQueue() => Interlocked.Increment(ref _queuePosition);

    public IReadOnlyList<ProxyEndPointInfo> GetEndPointInfos() => _entries.Select(x => x.Info).ToArray();

    private async Task<ProxyEndPointEntry[]> LoadEntries(CancellationToken cancellationToken)
    {
        var records = await store.List(cancellationToken).Vhc();
        var statusResetCount = Interlocked.Read(ref _statusResetCount);
        return records
            .Select(x => new ProxyEndPointEntry(x.ToInfo()) { StatusResetCount = statusResetCount })
            .ToArray();
    }

    public async Task Reload(CancellationToken cancellationToken)
    {
        // dropping the old entries also drops their dirty flags
        _entries = await LoadEntries(cancellationToken).Vhc();
        _queuePosition = await store.GetQueuePosition(cancellationToken).Vhc();
        IsEnabled = _entries.Length > 0;

        // the connections waiting for an enabled endpoint look again
        Interlocked.Exchange(ref _entriesReload,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    // After a reset of the statuses: the enabled states not saved yet, as a duplicate's success turns one off, with no
    // counts. The caller holds FlushLock
    public async Task SaveEnabledStates(CancellationToken cancellationToken)
    {
        var dirtyEntries = _entries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        var infos = dirtyEntries.Select(x => x.TakeEnabledStateForReset()).ToArray();
        try {
            await store.UpdateStatuses(infos, cancellationToken).Vhc();
        }
        catch {
            // left for the next save, as a failed flush leaves its changes: an endpoint turned off stays off
            foreach (var entry in dirtyEntries)
                entry.IsDirty = true;

            throw;
        }
    }

    public HashSet<(ProxyProtocol, string, int)> GetEnabledAddresses() => _entries
        .Where(x => x.EndPoint.IsEnabled)
        .Select(GetAddress)
        .ToHashSet();

    // The held entries as the store's reset left them, where the reload after it failed: the counts cleared, and the
    // enabled states, which the reset keeps, as they are
    public void ResetHeldStatuses()
    {
        // one whose enabled state a failed save left unsaved stays marked for the next save
        var statusResetCount = Interlocked.Read(ref _statusResetCount);
        _entries = _entries
            .Select(x => new ProxyEndPointEntry(new ProxyEndPointInfo {
                EndPoint = x.CopyEndPoint(),
                Status = new ProxyEndPointStatus()
            }) { StatusResetCount = statusResetCount, IsDirty = x.IsDirty })
            .ToArray();

        _queuePosition = 0;
    }

    // the caller holds FlushLock
    public async Task Flush(CancellationToken cancellationToken)
    {
        var dirtyEntries = _entries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        // copies that clear the flags, before the write: cleared after it, a change made during it would be lost
        var infos = dirtyEntries.Select(x => x.TakeForFlush()).ToArray();
        try {
            await store.UpdateStatuses(infos, cancellationToken).Vhc();
            await store.SetQueuePosition(_queuePosition, cancellationToken).Vhc();
        }
        catch {
            foreach (var entry in dirtyEntries)
                entry.IsDirty = true;
            throw;
        }
    }

    public IReadOnlyList<ProxyEndPointEntry> GetEnabledUnratedEntries() => _entries
        .Where(x => x.EndPoint.IsEnabled && x.Status.Quality is StatusQuality.Unknown)
        .ToArray();

    // never rated first, as the whole list's check is to rate them
    public IReadOnlyList<ProxyEndPointEntry> GetEnabledEntriesByQuality() => _entries
        .Where(x => x.EndPoint.IsEnabled)
        .OrderBy(x => x.Status.Quality)
        .ToArray();

    // Succeeded last first, the best rated first, as the connect waits for the first pass; then never used, then failed
    // last, which the quality's own order would not give, as it puts the never used first
    public IReadOnlyList<ProxyEndPointEntry> GetEnabledEntriesForConnectCheck() => _entries
        .Where(x => x.EndPoint.IsEnabled)
        .OrderBy(x => x.Status.IsLastUsedSucceeded ? 0 : x.Status.Quality is StatusQuality.Unknown ? 1 : 2)
        .ThenBy(x => x.Status.Quality)
        .ToArray();

    // the enabled endpoints the connection may take, each in its turn: an endpoint's next turn moves back by 3 for each
    // point of its penalty
    public IReadOnlyList<ProxyEndPointEntry> GetOrderedEntriesQuery(bool useRecentSucceeded) => _entries
        .Where(x => x.EndPoint.IsEnabled)
        .Where(x => !useRecentSucceeded || x.Status.LastSucceeded >= _sessionCreatedTime)
        .OrderBy(x => x.GetSortValue(_queuePosition))
        .ThenBy(x => x.Status.LastUsed)
        .ToArray();

    // Those that succeeded last first, each in its turn, then the rest in theirs. Each status read once: a check behind
    // the connect changes them meanwhile, and one read twice could fall in both groups, or in neither
    public static IReadOnlyList<ProxyEndPointEntry> SucceededFirst(IReadOnlyList<ProxyEndPointEntry> entries)
    {
        var isSucceeded = entries.Select(x => x.Status.IsLastUsedSucceeded).ToArray();
        return [
            .. entries.Where((_, i) => isSucceeded[i]),
            .. entries.Where((_, i) => !isSucceeded[i])
        ];
    }

    // an endpoint known by its address, as a reload makes its objects anew
    public static (ProxyProtocol, string, int) GetAddress(ProxyEndPointEntry entry) =>
        (entry.EndPoint.Protocol, entry.EndPoint.Host, entry.EndPoint.Port);

    // Turns off the other enabled endpoints of the winner's host and protocol, where the winner is still on, in one
    // step: two connections through duplicates at once would otherwise turn each other off, leaving the host none. One
    // read of the list for the winner and its duplicates
    public void DisableDuplicates(ProxyEndPointEntry entry)
    {
        lock (_duplicateLock) {
            var entries = _entries;
            if (FindCurrentEntry(entries, entry) is not { EndPoint.IsEnabled: true } winner)
                return;

            var url = winner.EndPoint.BuildUrlWithoutPassword();
            var duplicates = entries.Where(x =>
                x.EndPoint.IsEnabled &&
                x.EndPoint.Protocol == winner.EndPoint.Protocol &&
                x.EndPoint.Host.Equals(winner.EndPoint.Host, StringComparison.OrdinalIgnoreCase) &&
                x.EndPoint.Id != winner.EndPoint.Id);

            foreach (var duplicate in duplicates) {
                duplicate.EndPoint.IsEnabled = false;
                duplicate.Status.ErrorMessage = $"Duplicate IP disabled in favour of {url}";
                duplicate.IsDirty = true;
            }
        }
    }

    // The entry the connector holds now for one taken before, or null where the list lost it: a reload since made its
    // object anew, and a change to the old one is saved no more
    public ProxyEndPointEntry? TryGetCurrentEntry(ProxyEndPointEntry entry) =>
        FindCurrentEntry(_entries, entry);

    // found by its address's parts, which its Id hashes
    private static ProxyEndPointEntry? FindCurrentEntry(ProxyEndPointEntry[] entries, ProxyEndPointEntry entry)
    {
        var endPoint = entry.EndPoint;
        return Array.Find(entries, x => ReferenceEquals(x, entry)) ??
               Array.Find(entries, x =>
                   x.EndPoint.Protocol == endPoint.Protocol &&
                   x.EndPoint.Port == endPoint.Port &&
                   x.EndPoint.Host == endPoint.Host);
    }

    // the old one where the list lost it
    public ProxyEndPointEntry GetCurrentEntry(ProxyEndPointEntry entry) => TryGetCurrentEntry(entry) ?? entry;

    // onto the entry, at the queue's position now
    public void RecordSuccess(ProxyEndPointEntry entry, TimeSpan latency, TimeSpan? fastestLatency) =>
        entry.RecordSuccess(latency, fastestLatency, _queuePosition);

    public void RecordFailed(ProxyEndPointEntry entry, Exception error) => entry.RecordFailed(error, _queuePosition);
}
