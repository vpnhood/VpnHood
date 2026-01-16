using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.AppLib.Test.Tests;


public static class IpLocationSqlLiteBuilder
{
    // use LocalIpLocationProvider Deserialize to rebuild and compact db if changed
    // checksum inside archive is used to detect changes
    public static async Task Build(Stream sourceStream, string dbPath)
    {
        // ensure target folder exists
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory());

        // open connection once for all work
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // read checksum from archive and skip rebuild when already up to date
        sourceStream.Position = 0;
        await using var archive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true);
        var newChecksum = await ReadChecksum(archive);
        var oldChecksum = await GetChecksum(connection);
        if (oldChecksum == newChecksum)
            return;

        // reset schema
        await using (var cleanup = connection.CreateCommand()) {
            cleanup.CommandText = "DROP TABLE IF EXISTS IpLocations; DROP TABLE IF EXISTS Metadata;";
            await cleanup.ExecuteNonQueryAsync();
        }

        // create schema and indexes
        await using (var cmd = connection.CreateCommand()) {
            cmd.CommandText = @"
                CREATE TABLE IpLocations (
                    CountryCode TEXT NOT NULL,
                    StartIp BLOB NOT NULL,
                    EndIp BLOB NOT NULL
                );

                CREATE TABLE Metadata (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                CREATE INDEX idx_CountryCode ON IpLocations(CountryCode);
                CREATE INDEX idx_StartIp ON IpLocations(StartIp);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync()) {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO IpLocations (CountryCode, StartIp, EndIp) VALUES (@countryCode, @startIp, @endIp)";

            var countryCodeParam = cmd.Parameters.Add("@countryCode", SqliteType.Text);
            var startIpParam = cmd.Parameters.Add("@startIp", SqliteType.Blob);
            var endIpParam = cmd.Parameters.Add("@endIp", SqliteType.Blob);

            // stream each country file to keep memory low
            foreach (var entry in archive.Entries.Where(e => Path.GetExtension(e.Name) == ".ips")) {
                var countryCode = Path.GetFileNameWithoutExtension(entry.Name);
                await using var entryStream = await entry.OpenAsync();
                var ipRanges = IpRangeOrderedList.Deserialize(entryStream);

                foreach (var ipRange in ipRanges) {
                    countryCodeParam.Value = countryCode;
                    startIpParam.Value = ToBytes(ipRange.FirstIpAddress);
                    endIpParam.Value = ToBytes(ipRange.LastIpAddress);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // save metadata
            await using var metaCmd = connection.CreateCommand();
            metaCmd.Transaction = transaction;
            metaCmd.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES (@key, @value)";
            metaCmd.Parameters.AddWithValue("@key", "Checksum");
            metaCmd.Parameters.AddWithValue("@value", newChecksum);
            await metaCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }

        // compact database
        await using (var vacuumCmd = connection.CreateCommand()) {
            vacuumCmd.CommandText = "VACUUM";
            await vacuumCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string?> GetChecksum(SqliteConnection connection)
    {
        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Metadata'";
        var hasTable = (string?)await tableCmd.ExecuteScalarAsync();
        if (hasTable == null)
            return null;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Metadata WHERE Key = 'Checksum' LIMIT 1";
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private static async Task<string> ReadChecksum(ZipArchive archive)
    {
        var checksumEntry = archive.GetEntry("_checksum.txt") ?? throw new FileNotFoundException("checksum.txt not found in archive");
        await using var stream = await checksumEntry.OpenAsync();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        return content.Trim();
    }


    public static byte[] ToBytes(IPAddress ipAddress)
    {
        return IpLocationSqlLiteProvider.ToBytes(ipAddress);
    }

}