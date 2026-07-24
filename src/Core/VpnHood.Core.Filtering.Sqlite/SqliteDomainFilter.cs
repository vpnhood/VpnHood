using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Memory;

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
// greatest-prefix seek would miss an ancestor hiding behind a more specific sibling entry.
// The db is immutable at runtime (built once, opened read-only): which sets are populated is probed once
// at construction, so absent sets cost nothing. All threads share ONE read-only connection, serialized
// by a lock (same rationale as SqliteIpFilter: lookups are microsecond index seeks, CachedDomainFilter
// absorbs repeats, and a reader-per-thread design leaked one native SQLite connection per pool thread
// for the session lifetime). The connection is opened EAGERLY and held for the gate's lifetime: it pins
// the db while the app's manifest sweep runs — a locked file survives the sweep on Windows, and an
// already-open fd keeps working through the unlink on unix — so a live gate can never lose its db
// underneath itself.
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

    public event EventHandler? Changed;

    public SqliteDomainFilter(IDomainFilter? next, string dbPath, bool autoDisposeNextFilter = true)
    {
        SplitSqlite.EnsureInitialized();
        _next = next;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // this gate's own db is immutable; roll a change announced below it up the pipe
        if (next != null)
            next.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        _sharedReader = CreateReader(connectionString);
        _hasIncludes = HasRows(_sharedReader.Connection, FilterAction.Include);
        _hasExcludes = HasRows(_sharedReader.Connection, FilterAction.Exclude);
        _hasBlocks = HasRows(_sharedReader.Connection, FilterAction.Block);
    }

    // all sets empty and nothing below ⇒ the pipe can never decide; lets the client skip SNI extraction
    public bool IsEmpty => !(_hasIncludes || _hasExcludes || _hasBlocks) && (_next?.IsEmpty ?? true);

    public FilterAction Process(string? domain)
    {
        var normalizedDomain = DomainUtils.NormalizeDomain(domain);
        if (normalizedDomain.Length == 0)
            return FilterAction.Default;

        var invertedDomain = DomainUtils.InvertDomain(normalizedDomain);

        if (_hasBlocks && Contains(FilterAction.Block, invertedDomain))
            return FilterAction.Block;

        if (_hasExcludes && Contains(FilterAction.Exclude, invertedDomain))
            return FilterAction.Exclude;

        if (_hasIncludes && Contains(FilterAction.Include, invertedDomain))
            return FilterAction.Include;

        return _next?.Process(domain) ?? FilterAction.Default;
    }

    private bool Contains(FilterAction action, string invertedDomain)
    {
        lock (_readerLock) {
            // teardown race: a lookup that loses the race to Dispose answers "not a member" instead of
            // touching the disposed connection
            if (_disposed)
                return false;

            var command = _sharedReader.GetCommand(action);

            // probe the domain itself and every label ancestor ("com.google.www" → "com.google" → "com");
            // each candidate is one index seek, and the walk is as deep as the domain has labels
            for (var length = invertedDomain.Length; length > 0; length = invertedDomain.LastIndexOf('.', length - 1)) {
                command.Parameters[0].Value = invertedDomain[..length];
                if ((long)command.ExecuteScalar()! == 1)
                    return true;
            }

            return false;
        }
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

    // this gate's own db is immutable (a new db means a new gate); just forward the command
    public void Reconfigure() => _next?.Reconfigure();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

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
