using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
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
// at construction, so absent concerns cost nothing on the hot path. Each thread gets its own read-only
// connection with lazily prepared point queries; CachedIpFilter shields this from all but the first packet
// to a new endpoint.
public sealed class SqliteIpFilter : IIpFilter
{
    private readonly IIpFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    private readonly bool _hasIncludes;
    private readonly bool _hasExcludes;
    private readonly bool _hasBlocks;
    private readonly ThreadLocal<Reader> _reader;
    private readonly ConcurrentBag<Reader> _allReaders = [];
    private volatile bool _disposed;

    public SqliteIpFilter(IIpFilter? next, string dbPath, bool autoDisposeNextFilter = true)
    {
        SplitIpSqlite.EnsureInitialized();
        _next = next;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        // a set is one logical list spanning both families (only-v4 includes still constrain v6 addresses)
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        _hasIncludes = HasRows(connection, FilterAction.Include);
        _hasExcludes = HasRows(connection, FilterAction.Exclude);
        _hasBlocks = HasRows(connection, FilterAction.Block);

        _reader = new ThreadLocal<Reader>(() => {
            var reader = new Reader(connectionString);
            _allReaders.Add(reader);
            return reader;
        });
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
        var reader = _reader.Value!;
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

    private static bool HasRows(SqliteConnection connection, FilterAction action)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT EXISTS(SELECT 1 FROM {SplitIpDb.GetTableName(action, isV4: true)}) " +
            $"OR EXISTS(SELECT 1 FROM {SplitIpDb.GetTableName(action, isV4: false)})";
        return (long)command.ExecuteScalar()! == 1;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _reader.Dispose();
        while (_allReaders.TryTake(out var reader))
            reader.Dispose();
        if (_autoDisposeNextFilter)
            _next?.Dispose();
    }

    // Per-thread read-only connection with lazily prepared point-query statements (one per set + family).
    private sealed class Reader : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteCommand?[] _commands = new SqliteCommand?[8];

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
