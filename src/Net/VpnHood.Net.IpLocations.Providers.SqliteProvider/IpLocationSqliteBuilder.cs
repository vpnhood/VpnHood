using System.Data;
using System.IO.Compression;
using System.Net;
using Microsoft.Data.Sqlite;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Net.IpLocations.Providers.SqliteProvider;

public static class IpLocationSqliteBuilder
{
    // use LocalIpLocationProvider Deserialize to rebuild and compact db if changed
    // the zip's stamp (ZipStamp) is used to detect changes
    public static async Task Build(IAsset zip, string dbPath, CancellationToken cancellationToken)
    {
        // ensure target folder exists
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory());

        // open connection once for all work
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await Build(zip, connection, cancellationToken).Vhc();
    }

    // The zip is opened twice: for its stamp, 25 bytes, and again only to build. A stream that can
    // seek is read where it lies, as ZipArchive seeks for itself; one that cannot is copied into
    // memory first - what follows its stamp is no zip on its own, since the zip's offsets count it.
    public static async Task Build(IAsset zip, SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).Vhc();

        // read the zip's stamp and skip rebuild when already up to date
        var newChecksum = await ZipStamp.ReadAsync(zip, cancellationToken).Vhc();
        var oldChecksum = await GetChecksum(connection, cancellationToken).Vhc();
        if (oldChecksum == newChecksum)
            return;

        var zipStream = await zip.OpenReadAsync(cancellationToken).Vhc();
        await using var seekable = await zipStream.ToMemoryStreamIfNotSeekableAsync(cancellationToken).Vhc();
        await using var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: true);

        // reset schema
        await using (var cleanup = connection.CreateCommand()) {
            cleanup.CommandText = "DROP TABLE IF EXISTS IpLocations; DROP TABLE IF EXISTS Metadata;";
            await cleanup.ExecuteNonQueryAsync(cancellationToken).Vhc();
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
            await cmd.ExecuteNonQueryAsync(cancellationToken).Vhc();
        }

        await using (var transaction =
                     (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).Vhc()) {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText =
                "INSERT INTO IpLocations (CountryCode, StartIp, EndIp) VALUES (@countryCode, @startIp, @endIp)";

            var countryCodeParam = cmd.Parameters.Add("@countryCode", SqliteType.Text);
            var startIpParam = cmd.Parameters.Add("@startIp", SqliteType.Blob);
            var endIpParam = cmd.Parameters.Add("@endIp", SqliteType.Blob);

            // stream each country file to keep memory low
            foreach (var entry in archive.Entries.Where(e => Path.GetExtension(e.Name) == ".ips")) {
                var countryCode = Path.GetFileNameWithoutExtension(entry.Name);
                await using var entryStream = await entry.OpenAsync(cancellationToken).Vhc();
                var ipRanges = IpRangeOrderedList.Deserialize(entryStream);

                foreach (var ipRange in ipRanges) {
                    countryCodeParam.Value = countryCode;
                    startIpParam.Value = ToBytes(ipRange.FirstIpAddress);
                    endIpParam.Value = ToBytes(ipRange.LastIpAddress);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).Vhc();
                }
            }

            // save metadata
            await using var metaCmd = connection.CreateCommand();
            metaCmd.Transaction = transaction;
            metaCmd.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES (@key, @value)";
            metaCmd.Parameters.AddWithValue("@key", "Checksum");
            metaCmd.Parameters.AddWithValue("@value", newChecksum);
            await metaCmd.ExecuteNonQueryAsync(cancellationToken).Vhc();

            await transaction.CommitAsync(cancellationToken).Vhc();
        }

        // compact database
        await using (var vacuumCmd = connection.CreateCommand()) {
            vacuumCmd.CommandText = "VACUUM";
            await vacuumCmd.ExecuteNonQueryAsync(cancellationToken).Vhc();
        }
    }

    private static async Task<string?> GetChecksum(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Metadata'";
        var hasTable = (string?)await tableCmd.ExecuteScalarAsync(cancellationToken).Vhc();
        if (hasTable == null)
            return null;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Metadata WHERE Key = 'Checksum' LIMIT 1";
        return (string?)await cmd.ExecuteScalarAsync(cancellationToken).Vhc();
    }

    public static byte[] ToBytes(IPAddress ipAddress)
    {
        return IpLocationSqliteProvider.ToBytes(ipAddress);
    }
}
