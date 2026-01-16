using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;


public class IpLocationSqlLiteProvider : IIpRangeLocationProvider, IAsyncDisposable
{
    private readonly Lazy<SqliteConnection> _connection;

    private IpLocationSqlLiteProvider(string dbPath)
    {
        _connection = new Lazy<SqliteConnection>(() => OpenConnection(dbPath));
    }

    public static async Task<IpLocationSqlLiteProvider> Open(string dbPath)
    {
        var provider = new IpLocationSqlLiteProvider(dbPath);
        await provider._connection.Value.OpenAsync();
        return provider;
    }

    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        var ipBytes = ToBytes(ipAddress);
        await using var cmd = _connection.Value.CreateCommand();
        cmd.CommandText = "SELECT CountryCode FROM IpLocations WHERE @ipBytes BETWEEN StartIp AND EndIp LIMIT 1";
        cmd.Parameters.AddWithValue("@ipBytes", ipBytes);

        var countryCode = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
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

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, cancellationToken).Vhc()
            ?? await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, cancellationToken).Vhc()
            ?? throw new Exception("Could not find any public ip address.");

        return await GetLocation(ipAddress, cancellationToken);
    }

    public async Task<IpRangeOrderedList> GetIpRanges(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var ranges = new List<IpRange>();
        await using var cmd = _connection.Value.CreateCommand();
        cmd.CommandText = "SELECT StartIp, EndIp FROM IpLocations WHERE CountryCode = @countryCode COLLATE NOCASE ORDER BY StartIp";
        cmd.Parameters.AddWithValue("@countryCode", countryCode);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            var startIpBytes = (byte[])reader[0];
            var endIpBytes = (byte[])reader[1];
            ranges.Add(new IpRange(FromBytes(startIpBytes), FromBytes(endIpBytes)));
        }

        return new IpRangeOrderedList(ranges);
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        return new SqliteConnection(connectionString);
    }

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

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
            await _connection.Value.DisposeAsync();
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
            _connection.Value.Dispose();
    }
}
