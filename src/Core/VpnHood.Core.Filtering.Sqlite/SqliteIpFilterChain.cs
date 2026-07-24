using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// THE split-ip stage of the filter pipe: owns one SqliteIpFilter gate per db its folder's manifest
// lists (SplitDbManifest is the source of truth — a stray db file in the folder means nothing).
// Reconfigure re-reads the manifest and, only when the paths changed, swaps the gates — in-flight
// lookups drain on the old ones first, so disposal never races a lookup — deletes the superseded db
// files it had open and raises Changed so the caches above drop their verdicts. The permanent inner
// (next) filter is never touched by a swap.
public sealed class SqliteIpFilterChain : IIpFilter
{
    private readonly string _dbFolder;
    private readonly IIpFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    // lookups run concurrently (read); a swap drains them before disposing the old gates (write)
    private readonly ReaderWriterLockSlim _swapLock = new();
    private readonly Lock _reconfigureLock = new();
    private IIpFilter? _gates;
    private string[] _dbPaths;

    public event EventHandler? Changed;

    // no rules in any gate and none below ⇒ the pipe can never decide (re-checked by consumers on Changed)
    public bool IsEmpty {
        get {
            _swapLock.EnterReadLock();
            try {
                if (_gates is { IsEmpty: false })
                    return false;
            }
            finally {
                _swapLock.ExitReadLock();
            }

            return _next?.IsEmpty ?? true;
        }
    }

    public SqliteIpFilterChain(IIpFilter? next, string dbFolder, bool autoDisposeNextFilter = true)
    {
        _next = next;
        _dbFolder = dbFolder;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe
        if (next != null)
            next.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        // construction is not a change: build the initial gates directly — no swap, no Changed, no log
        _dbPaths = SplitDbManifest.Read(dbFolder);
        _gates = BuildGates(_dbPaths);
    }

    // the inner filter runs first and its veto wins; the gates only see traffic it passed as Default.
    // Composition-wise this equals chaining the gates directly over next (both consult next first),
    // but keeping next OUTSIDE the swap unit means a gate swap never touches it.
    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var result = _next?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        _swapLock.EnterReadLock();
        try {
            return _gates?.Process(protocol, endPoint) ?? FilterAction.Default;
        }
        finally {
            _swapLock.ExitReadLock();
        }
    }

    public void Reconfigure()
    {
        lock (_reconfigureLock) {
            var dbPaths = SplitDbManifest.Read(_dbFolder);
            if (!dbPaths.SequenceEqual(_dbPaths)) {
                VhLogger.Instance.LogInformation(
                    "SqliteIpFilterChain is swapping its split-ip gates. OldCount: {OldCount}, NewCount: {NewCount}",
                    _dbPaths.Length, dbPaths.Length);

                // build first (a failure keeps the current gates published), swap, then dispose the
                // drained old gates OUTSIDE the write lock so lookups on the new gates resume at once
                var newGates = BuildGates(dbPaths);
                Swap(newGates)?.Dispose();

                // delete exactly the files this stage just closed — this process held them open, so
                // nobody else could; files it never opened are the naming-scheme owner's business
                foreach (var supersededPath in _dbPaths.Except(dbPaths))
                    VhUtils.TryDeleteFile(supersededPath);

                _dbPaths = dbPaths;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // the command rolls DOWN the pipe
        _next?.Reconfigure();
    }

    // no Changed on teardown: the whole pipe is going away, nothing is left to invalidate.
    // _swapLock is intentionally not disposed: a lookup racing the pipe teardown may still enter
    // Process (and gets Default); the lock's wait handles are reclaimed by their finalizers
    public void Dispose()
    {
        Swap(null)?.Dispose();
        if (_autoDisposeNextFilter)
            _next?.Dispose();
    }

    // Only the pointer swap excludes lookups; the write lock waits for every in-flight lookup to
    // leave the old gates, so the caller can dispose them with nobody inside
    private IIpFilter? Swap(IIpFilter? gates)
    {
        _swapLock.EnterWriteLock();
        try {
            var oldGates = _gates;
            _gates = gates;
            return oldGates;
        }
        finally {
            _swapLock.ExitWriteLock();
        }
    }

    private static IIpFilter? BuildGates(string[] dbPaths)
    {
        IIpFilter? gates = null;
        try {
            foreach (var dbPath in dbPaths)
                gates = new SqliteIpFilter(gates, dbPath);
            return gates;
        }
        catch {
            // fail loud but leak nothing: the caller keeps its current gates and sees the error
            gates?.Dispose();
            throw;
        }
    }
}
