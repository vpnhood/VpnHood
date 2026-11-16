using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace VpnHood.Core.Client.ConnectorServices;

// The HTTP protocol each server took last in this process. A server that took 15 is never asked the legacy way
// again, so no 401 talks the app down from the proof there; one that took only the legacy way is asked that way
// first, and asked 15 again once it refuses that.
internal static class ServerProtocolMemory
{
    private static readonly ConcurrentDictionary<(IPEndPoint EndPoint, string Farm), bool> IsLegacyByServer = new();

    // null: not asked yet in this process
    public static bool? IsLegacy(IPEndPoint endPoint, byte[] farmSecret) =>
        IsLegacyByServer.TryGetValue(GetKey(endPoint, farmSecret), out var isLegacy) ? isLegacy : null;

    public static void Record(IPEndPoint endPoint, byte[] farmSecret, bool isLegacy) =>
        IsLegacyByServer[GetKey(endPoint, farmSecret)] = isLegacy;

    // a server is its endpoint in its farm: an address another farm's server takes over starts unknown
    private static (IPEndPoint, string) GetKey(IPEndPoint endPoint, byte[] farmSecret) =>
        (endPoint, Convert.ToHexString(SHA256.HashData(farmSecret)));
}
