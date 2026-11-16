using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Memory;

namespace VpnHood.Core.Filtering.Sqlite;

// Self-describing split-domain gate — the string twin of SqliteIpFilter: answers membership for the db's
// include/exclude/block domain sets. Unlike the IP gates this one may return Include: domains are more
// specific knowledge than IPs, so the include set is the explicit override lane that forces a domain
// through the tunnel past any IP-gate veto (e.g. "tunnel nothing except aaa.com" = include aaa.com here +
// exclude 0.0.0.0/0 in the ip gate). For the same reason its own sets are consulted BEFORE the next filter
// (mirroring StaticDomainFilter), where the IP gates consult next first.
//   block set:   member ⇒ Block   (drop entirely)
//   exclude set: member ⇒ Exclude (bypass the tunnel)
//   include set: member ⇒ Include (force through the tunnel, skip the ip gates)
// Every entry matches itself and its subdomains ("google.com" matches "mail.google.com"), so a lookup
// probes the inverted domain and each of its label ancestors with exact point queries — a single
// greatest-prefix seek would miss an ancestor hiding behind a more specific sibling entry. The nearest
// one that is in any set decides, so an entry under another is an exception to it; the sets' order above
// decides only between entries of the same domain.
// The db is immutable at runtime (built once, opened read-only): which sets are populated is probed once
// at construction, so absent sets cost nothing. All threads share ONE read-only connection, serialized
// by a lock (same rationale as SqliteIpFilter: lookups are microsecond index seeks, CachedDomainFilter
// absorbs repeats, and a reader-per-thread design leaked one native SQLite connection per pool thread
// for the session lifetime). The connection is opened EAGERLY and held for the gate's lifetime: it pins
// the db while the app's manifest sweep runs — a locked file survives the sweep on Windows, and an
// already-open fd keeps working after the file is unlinked on unix — so a live gate can never lose
// its db underneath itself.
/// <summary>
/// Provides a stage of a domain filter pipe that decides by the sets of a split domain database.
/// </summary>
/// <remarks>
/// <para>
/// Its own sets decide first: a name in the block set is blocked, one in the exclude set excluded, and one in the
/// include set included, which sends its flows through the tunnel past every IP filter; any other name gets the next
/// stage's verdict. An entry matches its domain and every subdomain, and the most specific entry that matches decides,
/// so an entry under another is an exception to it: with <c>google.com</c> excluded and <c>mail.google.com</c>
/// included, <c>mail.google.com</c> is included. Where the same domain is in more than one set, Block goes before
/// Exclude, and Exclude before Include. Names are compared as <see cref="DomainUtils.NormalizeDomain"/> gives them.
/// </para>
/// <para>
/// The database is opened read-only when the stage is made and held until its disposal, so it stays readable while a
/// manifest write deletes its file; which of its sets have rows is read once then, as it does not change. All
/// threads' lookups share its one connection, one at a time; a lookup after the disposal finds no member.
/// </para>
/// </remarks>
public sealed class SqliteDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    private readonly bool _hasIncludes;
    private readonly bool _hasExcludes;
    private readonly bool _hasBlocks;
    private readonly Lock _readerLock = new();
    private readonly Reader _sharedReader;
    private volatile bool _disposed;

    /// <inheritdoc/>
    /// <remarks>It occurs when the next stage's event does: the stage's own database does not change.</remarks>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDomainFilter"/> class, and opens its database.
    /// </summary>
    /// <param name="next">
    /// The stage that decides the names the database's sets do not match, or <see langword="null"/> to give them
    /// <see cref="FilterAction.Default"/>.
    /// </param>
    /// <param name="dbPath">The path of the split domain database.</param>
    /// <param name="autoDisposeNextFilter">
    /// <see langword="true"/> to dispose <paramref name="next"/> with this stage.
    /// </param>
    public SqliteDomainFilter(IDomainFilter? next, string dbPath, bool autoDisposeNextFilter = true)
    {
        SplitSqlite.EnsureInitialized();
        _next = next;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        _sharedReader = CreateReader(connectionString);
        try {
            _hasIncludes = HasRows(_sharedReader.Connection, FilterAction.Include);
            _hasExcludes = HasRows(_sharedReader.Connection, FilterAction.Exclude);
            _hasBlocks = HasRows(_sharedReader.Connection, FilterAction.Block);
        }
        // A file without the tables, as a db of another kind: no one gets this gate to dispose, so the
        // connection, which holds the file open, is closed here
        catch {
            _sharedReader.Dispose();
            VhTypeTracker.Record("SqliteDomainFilter.Reader.disposed");
            throw;
        }

        // This gate's own db is immutable; roll a change announced below it up the pipe. Last, as a gate that
        // failed above must not stay on the next stage's event
        if (next != null)
            next.Changed += Next_Changed;
    }

    private void Next_Changed(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <value>
    /// <see langword="true"/> while the database's three sets are empty and the next stage, if any, is empty too.
    /// </value>
    public bool IsEmpty => !HasEntries && (_next?.IsEmpty ?? true);

    private bool HasEntries => _hasIncludes || _hasExcludes || _hasBlocks;

    /// <summary>Decides what happens to a flow for a domain name, by the database's sets, then by the next stage.</summary>
    /// <param name="domain">The name, or <see langword="null"/>.</param>
    /// <returns>
    /// The verdict of the most specific entry that matches the name, or else the next stage's;
    /// <see cref="FilterAction.Default"/> for a null, empty or white-space name, which the next stage is not asked about.
    /// </returns>
    public FilterAction Process(string? domain)
    {
        var normalizedDomain = DomainUtils.NormalizeDomain(domain);
        if (normalizedDomain.Length == 0)
            return FilterAction.Default;

        // a db with no rows costs neither the lock nor a walk
        var verdict = HasEntries ? FindMostSpecific(DomainUtils.InvertDomain(normalizedDomain)) : null;
        return verdict ?? _next?.Process(domain) ?? FilterAction.Default;
    }

    // The domain itself, then each label ancestor, nearest first ("com.google.www" → "com.google" → "com"), so the
    // most specific entry decides; at each, the sets in their order. Each candidate is one index seek per set that
    // has rows, and the walk is as deep as the domain has labels.
    private FilterAction? FindMostSpecific(string invertedDomain)
    {
        lock (_readerLock) {
            // teardown race: a lookup that loses the race to Dispose finds no member instead of touching the
            // disposed connection
            if (_disposed)
                return null;

            for (var length = invertedDomain.Length; length > 0; length = invertedDomain.LastIndexOf('.', length - 1)) {
                var candidate = invertedDomain[..length];
                if (_hasBlocks && IsMember(FilterAction.Block, candidate))
                    return FilterAction.Block;

                if (_hasExcludes && IsMember(FilterAction.Exclude, candidate))
                    return FilterAction.Exclude;

                if (_hasIncludes && IsMember(FilterAction.Include, candidate))
                    return FilterAction.Include;
            }

            return null;
        }
    }

    // under the reader's lock
    private bool IsMember(FilterAction action, string invertedDomain)
    {
        var command = _sharedReader.GetCommand(action);
        command.Parameters[0].Value = invertedDomain;
        return (long)command.ExecuteScalar()! == 1;
    }

    private static Reader CreateReader(string connectionString)
    {
        var reader = new Reader(connectionString);
        VhTypeTracker.Track(reader, "SqliteDomainFilter.Reader");
        return reader;
    }

    private static bool HasRows(SqliteConnection connection, FilterAction action)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {SplitDomainDb.GetTableName(action)})";
        return (long)command.ExecuteScalar()! == 1;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage: its own database does not change.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) =>
        _next?.Reconfigure(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Closes the database, and disposes the next stage when this stage was made to.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // or a next stage that lives on would hold this gate
        if (_next != null)
            _next.Changed -= Next_Changed;

        lock (_readerLock) {
            _sharedReader.Dispose();
            VhTypeTracker.Record("SqliteDomainFilter.Reader.disposed");
        }

        if (_autoDisposeNextFilter)
            _next?.Dispose();
    }

    // The shared read-only connection with lazily prepared point-query statements (one per set).
    private sealed class Reader : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteCommand?[] _commands = new SqliteCommand?[4];

        public SqliteConnection Connection => _connection;

        public Reader(string connectionString)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
        }

        public SqliteCommand GetCommand(FilterAction action)
        {
            return _commands[(int)action] ??= CreateQuery(SplitDomainDb.GetTableName(action));
        }

        private SqliteCommand CreateQuery(string table)
        {
            var command = _connection.CreateCommand();
            command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE domain = $d)";
            command.Parameters.Add(command.CreateParameter());
            command.Parameters[0].ParameterName = "$d";
            command.Prepare();
            return command;
        }

        public void Dispose()
        {
            foreach (var command in _commands)
                command?.Dispose();
            _connection.Dispose();
        }
    }
}
