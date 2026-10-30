using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// THE split-domain stage of the filter pipe — the domain twin of SqliteIpFilterChain: owns one
// SqliteDomainFilter gate per db its folder's manifest lists (SplitDbManifest is the source of truth —
// a stray db file in the folder means nothing). Reconfigure re-reads the manifest and, only when the
// paths changed, swaps the gates — in-flight lookups drain on the old ones first, so disposal never
// races a lookup — deletes the superseded db files it had open and raises Changed so the caches above
// drop their verdicts and the client re-derives SNI extraction from IsEmpty. The permanent inner
// (next) filter is never touched by a swap.
public sealed class SqliteDomainFilterChain : IDomainFilter
{
    private readonly string _dbFolder;
    private readonly IDomainFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    // lookups run concurrently (read); a swap drains them before disposing the old gates (write)
    private readonly ReaderWriterLockSlim _swapLock = new();
    private readonly AsyncLock _reconfigureLock = new();
    private IDomainFilter? _gates;
    private string[] _dbPaths;
    private bool _disposed; // set with the close's swap, under the write lock

    public event EventHandler? Changed;

    // the chain of the dbs its folder's manifest lists now, which it reads first
    public static async Task<SqliteDomainFilterChain> Create(IDomainFilter? next, string dbFolder,
        bool autoDisposeNextFilter, CancellationToken cancellationToken)
    {
        var dbPaths = await SplitDbManifest.Read(dbFolder, cancellationToken).Vhc();
        return new SqliteDomainFilterChain(next, dbFolder, dbPaths, autoDisposeNextFilter);
    }

    private SqliteDomainFilterChain(IDomainFilter? next, string dbFolder, string[] dbPaths,
        bool autoDisposeNextFilter)
    {
        _next = next;
        _dbFolder = dbFolder;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe
        if (next != null)
            next.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        // construction is not a change: build the initial gates directly — no swap, no Changed, no log
        _dbPaths = dbPaths;
        _gates = BuildGates(_dbPaths);
    }

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

    // domain gates consult their own sets BEFORE next (an include override must not be second-guessed),
    // so the gates run first and next only sees domains they passed as Default. Composition-wise this
    // equals chaining the gates directly over next, but keeping next OUTSIDE the swap unit means a gate
    // swap never touches it.
    public FilterAction Process(string? domain)
    {
        _swapLock.EnterReadLock();
        try {
            var result = _gates?.Process(domain) ?? FilterAction.Default;
            if (result != FilterAction.Default)
                return result;
        }
        finally {
            _swapLock.ExitReadLock();
        }

        return _next?.Process(domain) ?? FilterAction.Default;
    }

    public async Task Reconfigure(CancellationToken cancellationToken)
    {
        // the chain closed meanwhile: nothing else follows
        if (!await SwapGates(cancellationToken).Vhc())
            return;

        // the command rolls DOWN the pipe
        if (_next != null)
            await _next.Reconfigure(cancellationToken).Vhc();
    }

    // the gates of the manifest's dbs, swapped in when its paths changed; false once the chain is closed
    private async Task<bool> SwapGates(CancellationToken cancellationToken)
    {
        using var scopeLock = await _reconfigureLock.LockAsync(cancellationToken).Vhc();
        var dbPaths = await SplitDbManifest.Read(_dbFolder, cancellationToken).Vhc();
        if (dbPaths.SequenceEqual(_dbPaths))
            return true;

        VhLogger.Instance.LogInformation(
            "SqliteDomainFilterChain is swapping its split-domain gates. OldCount: {OldCount}, NewCount: {NewCount}",
            _dbPaths.Length, dbPaths.Length);

        // build first (a failure keeps the current gates published), swap, then dispose the
        // drained old gates OUTSIDE the write lock so lookups on the new gates resume at once
        var newGates = BuildGates(dbPaths);
        if (!TrySwap(newGates, close: false, out var oldGates)) {
            // the new gates close with the chain
            newGates?.Dispose();
            return false;
        }

        oldGates?.Dispose();

        // delete exactly the files this stage just closed — this process held them open, so
        // nobody else could; files it never opened are the naming-scheme owner's business
        foreach (var supersededPath in _dbPaths.Except(dbPaths))
            VhUtils.TryDeleteFile(supersededPath);

        _dbPaths = dbPaths;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // no Changed on teardown: the whole pipe is going away, nothing is left to invalidate.
    // _swapLock is intentionally not disposed: a lookup racing the pipe teardown may still enter
    // Process (and gets Default); the lock's wait handles are reclaimed by their finalizers
    public void Dispose()
    {
        if (!TrySwap(null, close: true, out var gates))
            return;

        gates?.Dispose();
        if (_autoDisposeNextFilter)
            _next?.Dispose();
    }

    // Only the pointer swap excludes lookups; the write lock waits for every in-flight lookup to
    // leave the old gates, so the caller can dispose them with nobody inside. A closed chain takes no
    // new gates: a reconfigure under way would leave them open after the close
    private bool TrySwap(IDomainFilter? gates, bool close, out IDomainFilter? oldGates)
    {
        _swapLock.EnterWriteLock();
        try {
            if (_disposed) {
                oldGates = null;
                return false;
            }

            _disposed = close;
            oldGates = _gates;
            _gates = gates;
            return true;
        }
        finally {
            _swapLock.ExitWriteLock();
        }
    }

    private static IDomainFilter? BuildGates(string[] dbPaths)
    {
        IDomainFilter? gates = null;
        try {
            foreach (var dbPath in dbPaths)
                gates = new SqliteDomainFilter(gates, dbPath);
            return gates;
        }
        catch {
            // fail loud but leak nothing: the caller keeps its current gates and sees the error
            gates?.Dispose();
            throw;
        }
    }
}
