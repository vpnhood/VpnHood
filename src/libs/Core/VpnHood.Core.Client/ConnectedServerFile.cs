using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client;

// The server of the session that is up, on disk until the client closes. A VPN host's crash closes nothing: Windows
// holds its connection, whose routes still send everything but this server into the dead tunnel, so the redial can
// reach this server only (ServerFinder tries it first).
// A client's close deletes its own record, not one a newer client of the process wrote since, as a forced reconnect
// closes the old client in the background, often after the new one has written its record; with it go the records no
// client of the process wrote, as a crash leaves under any key. A client writes nothing after its close.
internal class ConnectedServerFile(string folderPath, string tokenId)
{
    // the process's clients' writes and closes, one at a time, and the client that last wrote each record, by its path,
    // whose case Windows and macOS do not tell apart: two clients may name one folder in two ways
    private static readonly Lock FolderLock = new();
    private static readonly Dictionary<string, ConnectedServerFile> Writers = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    // named by the key's ID, which anyone can write
    private readonly string _filePath =
        Path.GetFullPath(Path.Combine(folderPath, FileUtils.ReplaceInvalidFileNameChars(tokenId) + ".json"));

    private bool _isClosed;

    // a file an update left mid-session may not parse: that is no server to try, not a failed connect
    public Task<VpnEndPoint?> Read(CancellationToken cancellationToken)
    {
        return JsonUtils.TryDeserializeFileAsync<VpnEndPoint>(_filePath, cancellationToken);
    }

    // nothing once the client's close has begun (Retire): a write after it would be the record's last, which the close then
    // deletes, a newer client's own write gone with it
    public void Write(VpnEndPoint vpnEndPoint)
    {
        lock (FolderLock) {
            if (_isClosed)
                return;

            Directory.CreateDirectory(folderPath);
            FileUtils.WriteAllTextAtomic(_filePath, JsonSerializer.Serialize(vpnEndPoint));
            Writers[_filePath] = this;
        }
    }

    // At the client's close's start, which a graceful close's teardown may keep from its end for a while
    public void Retire()
    {
        lock (FolderLock)
            _isClosed = true;
    }

    // In the client's close, which must run to its end.
    public void Close()
    {
        lock (FolderLock) {
            _isClosed = true;
            try {
                if (!Directory.Exists(folderPath))
                    return;

                foreach (var filePath in Directory.EnumerateFiles(folderPath).Select(Path.GetFullPath).ToArray()) {
                    if (Writers.TryGetValue(filePath, out var writer) && writer != this)
                        continue;

                    File.Delete(filePath);
                    Writers.Remove(filePath);
                }

                // nothing left behind where no newer client's record is
                if (!Directory.EnumerateFileSystemEntries(folderPath).Any())
                    Directory.Delete(folderPath);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not delete the connected servers' records.");
            }
        }
    }
}
