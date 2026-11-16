using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.IpLocations.Providers.SqliteProvider;

/// <summary>
/// Provides locations, the country only, and each country's IP ranges, from the SQLite database
/// <see cref="IpLocationSqliteBuilder"/> builds.
/// </summary>
public class IpLocationSqliteProvider : IIpRangeLocationProvider, IAsyncDisposable
{
    private readonly Lazy<SqliteConnection> _connection;
    private readonly bool _disposeConnection;
    private readonly string? _userAgent;
    private bool _disposed;

    private IpLocationSqliteProvider(string dbPath, string? userAgent)
    {
        _disposeConnection = true;
        _connection = new Lazy<SqliteConnection>(() => OpenConnection(dbPath));
        _userAgent = userAgent;
    }

    private IpLocationSqliteProvider(SqliteConnection connection, bool disposeConnection, string? userAgent)
    {
        _disposeConnection = disposeConnection;
        _connection = new Lazy<SqliteConnection>(() => connection);
        _userAgent = userAgent;
    }

    /// <summary>Opens a provider on a database file, read-only.</summary>
    /// <param name="dbPath">The database file.</param>
    /// <param name="userAgent">The User-Agent header the current location's public-IP lookup sends, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the provider, which owns its connection.</returns>
    /// <exception cref="SqliteException">The file cannot be opened.</exception>
    public static async Task<IpLocationSqliteProvider> Open(string dbPath, string? userAgent = null)
    {
        var provider = new IpLocationSqliteProvider(dbPath, userAgent);
        await provider._connection.Value.OpenAsync().Vhc();
        return provider;
    }

    /// <summary>Opens a provider on a connection, which it opens when it is closed.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave the connection to the caller when the provider is disposed;
    /// <see langword="false"/> to dispose it with the provider.
    /// </param>
    /// <param name="userAgent">The User-Agent header the current location's public-IP lookup sends, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the provider.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="SqliteException">The connection cannot be opened.</exception>
    public static async Task<IpLocationSqliteProvider> Open(SqliteConnection connection, bool leaveOpen = false,
        string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var provider = new IpLocationSqliteProvider(connection, disposeConnection: !leaveOpen, userAgent);
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync().Vhc();

        return provider;
    }

    /// <inheritdoc/>
    /// <remarks>An IPv4-mapped IPv6 address is looked up as IPv4.</remarks>
    /// <exception cref="KeyNotFoundException">No range holds the address.</exception>
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        var ipBytes = ToBytes(ipAddress);
        await using var cmd = _connection.Value.CreateCommand();
        cmd.CommandText = "SELECT CountryCode FROM IpLocations WHERE @ipBytes BETWEEN StartIp AND EndIp LIMIT 1";
        cmd.Parameters.AddWithValue("@ipBytes", ipBytes);

        var countryCode = (string?)await cmd.ExecuteScalarAsync(cancellationToken).Vhc();
        if (countryCode == null)
            throw new KeyNotFoundException($"Could not find location for given ip. IpAddress: {ipAddress}.");

        countryCode = countryCode.ToUpperInvariant();
        return new IpLocation {
            CountryName = new RegionInfo(countryCode).EnglishName,
            CountryCode = countryCode,
            IpAddress = ipAddress,
            CityName = null,
            RegionName = null
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It finds the device's public IP address online, IPv4 first, then looks the address up in the database.
    /// </remarks>
    /// <exception cref="Exception">No public IP address was found.</exception>
    /// <exception cref="KeyNotFoundException">No range holds the address.</exception>
    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, _userAgent, cancellationToken).Vhc()
            ?? await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, _userAgent, cancellationToken).Vhc()
            ?? throw new Exception("Could not find any public ip address.");

        return await GetLocation(ipAddress, cancellationToken).Vhc();
    }

    /// <inheritdoc/>
    /// <remarks>The code's case does not matter; an unknown country has no ranges.</remarks>
    /// <exception cref="ArgumentException"><paramref name="countryCode"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="countryCode"/> is <see langword="null"/>.</exception>
    public async Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var ranges = new List<IpRange>();
        await using var cmd = _connection.Value.CreateCommand();
        cmd.CommandText =
            "SELECT StartIp, EndIp FROM IpLocations WHERE CountryCode = @countryCode COLLATE NOCASE ORDER BY StartIp";
        cmd.Parameters.AddWithValue("@countryCode", countryCode);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).Vhc();
        while (await reader.ReadAsync(cancellationToken).Vhc()) {
            var startIpBytes = (byte[])reader[0];
            var endIpBytes = (byte[])reader[1];
            ranges.Add(new IpRange(FromBytes(startIpBytes), FromBytes(endIpBytes)));
        }

        return new IpRangeOrderedList(ranges);
    }

    /// <inheritdoc/>
    public async Task<string[]> GetCountryCodes(CancellationToken cancellationToken)
    {
        var countryCodes = new List<string>();
        await using var cmd = _connection.Value.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT CountryCode FROM IpLocations ORDER BY CountryCode";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).Vhc();
        while (await reader.ReadAsync(cancellationToken).Vhc()) {
            var countryCode = (string)reader[0];
            countryCodes.Add(countryCode.ToUpperInvariant());
        }

        return [.. countryCodes];
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    /// <summary>
    /// Converts an IP address to the database's key form: its 16 IPv6 bytes, an IPv4 address mapped to IPv6 first.
    /// </summary>
    /// <param name="ipAddress">The IP address.</param>
    /// <returns>The 16 bytes.</returns>
    public static byte[] ToBytes(IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetwork
            ? ipAddress.MapToIPv6().GetAddressBytes()
            : ipAddress.GetAddressBytes();
    }

    private static IPAddress FromBytes(byte[] bytes)
    {
        var ipAddress = new IPAddress(bytes);
        return ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;
    }

    /// <summary>Disposes the provider, as <see cref="Dispose"/> does; the task is already complete.</summary>
    /// <returns>A completed task.</returns>
    // the connection's one close, which is not thread-safe, is the quick one: it is local, with nothing to wait for
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disposes the connection, unless the caller keeps it. A failure to close the connection is logged, not thrown.
    /// Calling it again does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) || !_connection.IsValueCreated || !_disposeConnection)
            return;

        // a caller's connection may still hold a transaction or a reader, whose close throws
        _connection.Value.TryDispose(LogLevel.Warning);
    }
}