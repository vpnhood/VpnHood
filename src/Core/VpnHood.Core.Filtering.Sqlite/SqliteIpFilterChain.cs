using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Filtering.Sqlite;

// THE split-ip stage of the filter pipe: owns one SqliteIpFilter gate per db its folder's manifest
// lists (SplitDbManifest is the source of truth — a stray db file in the folder means nothing).
// Reconfigure re-reads the manifest and, only when the paths changed, swaps the gates — in-flight
// lookups drain on the old ones first, so disposal never races a lookup — deletes the superseded db
// files it had open and raises Changed so the caches above drop their verdicts. The permanent inner
// (next) filter is never touched by a swap.
/// <summary>
/// Provides the stage of an IP filter pipe that holds a <see cref="SqliteIpFilter"/> for each database a folder's
/// manifest lists, and swaps them when the manifest changes.
/// </summary>
/// <remarks>
/// The next stage decides first, and its verdict stands unless it is <see cref="FilterAction.Default"/>; then the
/// databases do, in the manifest's order, each only for what those before it left at
/// <see cref="FilterAction.Default"/>. A swap never touches the next stage. See <see cref="SplitDbManifest"/>.
/// </remarks>
public sealed class SqliteIpFilterChain : IIpFilter
{
    private readonly string _dbFolder;
    private readonly IIpFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    // lookups run concurrently (read); a swap drains them before disposing the old gates (write)
    private readonly ReaderWriterLockSlim _swapLock = new();
    private readonly AsyncLock _reconfigureLock = new();
    private IIpFilter? _gates;
    private string[] _dbPaths;
    private bool _disposed; // set with the close's swap, under the write lock

    /// <inheritdoc/>
    /// <remarks>It occurs after a swap of the databases, and when the next stage's event does.</remarks>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    /// <value><see langword="true"/> while every database's sets are empty and the next stage, if any, is empty too.</value>
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

    /// <summary>
    /// Creates the stage for a folder: it reads the folder's manifest and opens each database it lists.
    /// </summary>
    /// <param name="next">The stage that decides first, which no swap touches, or <see langword="null"/>.</param>
    /// <param name="dbFolder">The folder of the manifest and its databases.</param>
    /// <param name="autoDisposeNextFilter"><see langword="true"/> to dispose <paramref name="next"/> with this stage.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the stage.</returns>
    /// <exception cref="System.Text.Json.JsonException">The manifest is not valid JSON.</exception>
    /// <exception cref="InvalidDataException">The manifest is the JSON <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<SqliteIpFilterChain> Create(IIpFilter? next, string dbFolder,
        bool autoDisposeNextFilter, CancellationToken cancellationToken)
    {
        var dbPaths = await SplitDbManifest.Read(dbFolder, cancellationToken).Vhc();
        return new SqliteIpFilterChain(next, dbFolder, dbPaths, autoDisposeNextFilter);
    }

    private SqliteIpFilterChain(IIpFilter? next, string dbFolder, string[] dbPaths, bool autoDisposeNextFilter)
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

    // the inner filter runs first and its veto wins; the gates only see traffic it passed as Default.
    // Composition-wise this equals chaining the gates directly over next (both consult next first),
    // but keeping next OUTSIDE the swap unit means a gate swap never touches it.
    /// <inheritdoc/>
    /// <remarks>The next stage decides first, then the databases, in the manifest's order.</remarks>
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

    /// <inheritdoc/>
    /// <remarks>
    /// It reads the manifest again. When the list of databases has changed, it opens the new ones, swaps them in once
    /// the lookups under way on the old ones have left, closes the old ones, tries to delete the files of those the new
    /// list leaves out, and raises <see cref="Changed"/>; when a new database cannot be opened, the old ones stay and
    /// the error is thrown. Then it passes the call to the next stage. A disposed stage takes no new databases.
    /// </remarks>
    /// <exception cref="System.Text.Json.JsonException">The manifest is not valid JSON.</exception>
    /// <exception cref="InvalidDataException">The manifest is the JSON <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
            "SqliteIpFilterChain is swapping its split-ip gates. OldCount: {OldCount}, NewCount: {NewCount}",
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
    /// <summary>
    /// Closes the databases, without raising <see cref="Changed"/>, and disposes the next stage when this stage was
    /// made to. A lookup after it gets the next stage's verdict, or <see cref="FilterAction.Default"/>.
    /// </summary>
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
    private bool TrySwap(IIpFilter? gates, bool close, out IIpFilter? oldGates)
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
