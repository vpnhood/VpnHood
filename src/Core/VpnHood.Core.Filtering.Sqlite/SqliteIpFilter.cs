using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Sqlite;

// Self-describing split-ip gate: answers membership for the db's include/exclude/block sets. Like every
// stage in the client pipe it only vetoes (Exclude/Block) or passes (Default = "no objection"; undecided
// traffic tunnels). Include is NEVER returned here — it is reserved as an explicit override lane (domain
// force-list, ICMP force), and an inner Include would short-circuit outer gates.
//   block set:   member ⇒ Block   (drop entirely)
//   exclude set: member ⇒ Exclude (bypass the tunnel)
//   include set: non-empty and NOT a member ⇒ Exclude (chained include gates compose as intersection)
// The db is immutable at runtime (built once, opened read-only): which sets are populated is probed once
// at construction, so absent concerns cost nothing on the hot path. All threads share ONE read-only
// connection, serialized by a lock: lookups are microsecond index seeks and CachedIpFilter shields this
// from all but the first packet to a new endpoint, so contention is negligible — while a
// reader-per-thread design pins one native SQLite connection (fd + page cache) per pool thread for the
// whole session, which on iOS grew without bound as the ThreadPool churned workers. The connection is
// opened EAGERLY and held for the gate's lifetime: it pins the db while the app's manifest sweep runs —
// a locked file survives the sweep on Windows, and an already-open fd keeps working through the unlink
// on unix — so a live gate can never lose its db underneath itself.
public sealed class SqliteIpFilter : IIpFilter
{
    private readonly IIpFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    private readonly bool _hasIncludes;
    private readonly bool _hasExcludes;
    private readonly bool _hasBlocks;
    private readonly Lock _readerLock = new();
    private readonly Reader _sharedReader;
    private volatile bool _disposed;

    public event EventHandler? Changed;

    public SqliteIpFilter(IIpFilter? next, string dbPath, bool autoDisposeNextFilter = true)
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

        // a set is one logical list spanning both families (only-v4 includes still constrain v6 addresses)
        _hasIncludes = HasRows(_sharedReader.Connection, FilterAction.Include);
        _hasExcludes = HasRows(_sharedReader.Connection, FilterAction.Exclude);
        _hasBlocks = HasRows(_sharedReader.Connection, FilterAction.Block);
    }

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var result = _next?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        var address = endPoint.Address;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (_hasBlocks && Contains(FilterAction.Block, address))
            return FilterAction.Block;

        if (_hasExcludes && Contains(FilterAction.Exclude, address))
            return FilterAction.Exclude;

        if (_hasIncludes && !Contains(FilterAction.Include, address))
            return FilterAction.Exclude;

        return FilterAction.Default;
    }

    private bool Contains(FilterAction action, IPAddress address)
    {
        lock (_readerLock) {
            // teardown race: a lookup that loses the race to Dispose answers "not a member" instead of
            // touching the disposed connection
            if (_disposed)
                return false;

            var reader = _sharedReader;
            if (address.AddressFamily == AddressFamily.InterNetwork) {
                Span<byte> bytes = stackalloc byte[4];
                address.TryWriteBytes(bytes, out _);
                var key = SplitIpDb.ToV4Key(bytes);
                var command = reader.GetCommand(action, isV4: true);
                command.Parameters[0].Value = key;
                using var row = command.ExecuteReader();
                return row.Read() && row.GetInt64(0) >= key; // start_ip <= key already; check end_ip
            }
            else {
                var bytes = address.GetAddressBytes(); // 16-byte big-endian
                var command = reader.GetCommand(action, isV4: false);
                command.Parameters[0].Value = bytes;
                using var row = command.ExecuteReader();
                if (!row.Read())
                    return false;
                var end = (byte[])row.GetValue(0);
                return end.AsSpan().SequenceCompareTo(bytes) >= 0; // memcmp: end_ip >= addr
            }
        }
    }

    private static Reader CreateReader(string connectionString)
    {
        var reader = new Reader(connectionString);
        VhTypeTracker.Track(reader, "SqliteIpFilter.Reader");
        return reader;
    }

    private static bool HasRows(SqliteConnection connection, FilterAction action)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT EXISTS(SELECT 1 FROM {SplitIpDb.GetTableName(action, isV4: true)}) " +
            $"OR EXISTS(SELECT 1 FROM {SplitIpDb.GetTableName(action, isV4: false)})";
        return (long)command.ExecuteScalar()! == 1;
    }

    // this gate's own db is immutable (a new db means a new gate); just forward the command
    public void Reconfigure() => _next?.Reconfigure();

    // all sets empty and nothing below ⇒ the pipe can never decide
    public bool IsEmpty => !(_hasIncludes || _hasExcludes || _hasBlocks) && (_next?.IsEmpty ?? true);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_readerLock) {
            _sharedReader.Dispose();
            VhTypeTracker.Record("SqliteIpFilter.Reader.disposed");
        }

        if (_autoDisposeNextFilter)
            _next?.Dispose();
    }

    // The shared read-only connection with lazily prepared point-query statements (one per set + family).
    private sealed class Reader : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteCommand?[] _commands = new SqliteCommand?[8];

        public SqliteConnection Connection => _connection;

        public Reader(string connectionString)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
        }

        public SqliteCommand GetCommand(FilterAction action, bool isV4)
        {
            var index = ((int)action << 1) | (isV4 ? 1 : 0);
            return _commands[index] ??= CreateQuery(SplitIpDb.GetTableName(action, isV4));
        }

        private SqliteCommand CreateQuery(string table)
        {
            var command = _connection.CreateCommand();
            // greatest start_ip <= @a (single index seek); caller checks its end_ip
            command.CommandText = $"SELECT end_ip FROM {table} WHERE start_ip <= $a ORDER BY start_ip DESC LIMIT 1";
            command.Parameters.Add(command.CreateParameter());
            command.Parameters[0].ParameterName = "$a";
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
