using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client;

// The server of the session that is up, on disk until the client closes. A VPN host's crash closes nothing: Windows
// holds its connection, whose routes still send everything but this server into the dead tunnel, so the redial can
// reach this server only (ServerFinder tries it first).
internal class ConnectedServerFile(string folderPath, string tokenId)
{
    // named by the key's ID, which anyone can write
    private readonly string _filePath =
        Path.Combine(folderPath, FileUtils.ReplaceInvalidFileNameChars(tokenId) + ".json");

    // a file an update left mid-session may not parse: that is no server to try, not a failed connect
    public Task<VpnEndPoint?> Read(CancellationToken cancellationToken)
    {
        return JsonUtils.TryDeserializeFileAsync<VpnEndPoint>(_filePath, cancellationToken);
    }

    public Task Write(VpnEndPoint vpnEndPoint, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        return FileUtils.WriteAllTextAtomicAsync(_filePath, JsonSerializer.Serialize(vpnEndPoint), cancellationToken);
    }

    // In the client's close, which must run to its end. The whole folder, so what a crash under another key left goes
    // with it.
    public void TryDelete()
    {
        try {
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not delete the connected server's folder.");
        }
    }
}
