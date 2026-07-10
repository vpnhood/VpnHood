using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Sqlite;

// Lean country gate: answers "is this address in the selected-country set?" from an on-disk SQLite db and
// never returns Include (only the terminal granter does; tunneling is expressed as Default so outer gates
// still apply). The configured action decides what membership means:
//   Default => Default (db is never queried)
//   Include => member ? Default : Exclude   (tunnel only the selected set; the rest bypasses)
//   Exclude => member ? Exclude : Default   (bypass the selected set; the rest tunnels)
//   Block   => member ? Block   : Default   (drop the selected set; the rest tunnels)
// Other concerns (server routability, blocks, app includes/excludes) belong in their own pipe stages, not here.
//
// The db is immutable at runtime (built once, opened read-only). Each thread gets its own read-only connection
// with prepared statements; CachedIpFilter shields this from all but the first packet to a new endpoint.
public sealed class SqliteIpFilter : IIpFilter
{
    private readonly IIpFilter? _next;
    private readonly bool _autoDisposeNextFilter;
    private readonly FilterAction _action;
    private readonly string _connectionString;
    private readonly ThreadLocal<Reader> _reader;
    private readonly ConcurrentBag<Reader> _allReaders = new();
    private volatile bool _disposed;

    public SqliteIpFilter(IIpFilter? next, string dbPath, FilterAction action, bool autoDisposeNextFilter = true)
    {
        SplitIpSqlite.EnsureInitialized();
        _next = next;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        _action = action;
        _connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        _reader = new ThreadLocal<Reader>(() => {
            var reader = new Reader(_connectionString);
            _allReaders.Add(reader);
            return reader;
        });
    }

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var result = _next?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        if (_action == FilterAction.Default)
            return FilterAction.Default; // no action configured; skip the db check

        var member = Contains(endPoint.Address);
        return _action switch {
            FilterAction.Include => member ? FilterAction.Default : FilterAction.Exclude,
            FilterAction.Exclude => member ? FilterAction.Exclude : FilterAction.Default,
            FilterAction.Block => member ? FilterAction.Block : FilterAction.Default,
            _ => FilterAction.Default
        };
    }

    private bool Contains(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var reader = _reader.Value!;
        if (address.AddressFamily == AddressFamily.InterNetwork) {
            Span<byte> bytes = stackalloc byte[4];
            address.TryWriteBytes(bytes, out _);
            var key = SplitIpDb.ToV4Key(bytes);
            reader.V4.Parameters[0].Value = key;
            using var row = reader.V4.ExecuteReader();
            return row.Read() && row.GetInt64(0) >= key; // start_ip <= key already; check end_ip
        }
        else {
            var bytes = address.GetAddressBytes(); // 16-byte big-endian
            reader.V6.Parameters[0].Value = bytes;
            using var row = reader.V6.ExecuteReader();
            if (!row.Read())
                return false;
            var end = (byte[])row.GetValue(0);
            return end.AsSpan().SequenceCompareTo(bytes) >= 0; // memcmp: end_ip >= addr
        }
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

    // Per-thread read-only connection with prepared point-query statements (one per address family).
    private sealed class Reader : IDisposable
    {
        public readonly SqliteConnection Connection;
        public readonly SqliteCommand V4;
        public readonly SqliteCommand V6;

        public Reader(string connectionString)
        {
            Connection = new SqliteConnection(connectionString);
            Connection.Open();
            V4 = CreateQuery("range_v4");
            V6 = CreateQuery("range_v6");
        }

        private SqliteCommand CreateQuery(string table)
        {
            var command = Connection.CreateCommand();
            // greatest start_ip <= @a (single index seek); caller checks its end_ip
            command.CommandText = $"SELECT end_ip FROM {table} WHERE start_ip <= $a ORDER BY start_ip DESC LIMIT 1";
            command.Parameters.Add(command.CreateParameter());
            command.Parameters[0].ParameterName = "$a";
            command.Prepare();
            return command;
        }

        public void Dispose()
        {
            V4.Dispose();
            V6.Dispose();
            Connection.Dispose();
        }
    }
}
