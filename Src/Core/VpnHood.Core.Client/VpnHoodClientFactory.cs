using System.Text.Json;
using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client;

public class VpnHoodClientFactory
{
    private static string ConfigFilePath => Path.Combine(Directory.GetCurrentDirectory(), ClientOptions.VpnConfigFileName);
    private static string StatusFilePath => Path.Combine(Directory.GetCurrentDirectory(), ClientOptions.VpnStatusFileName);

    public static ClientOptions ReadClientOptions()
    {
        // read from config file
        var json = File.ReadAllText(ConfigFilePath);
        return JsonUtils.Deserialize<ClientOptions>(json);
    }

    public static VpnHoodClient Create(IVpnAdapter vpnAdapter, ISocketFactory socketFactory, ITracker? tracker)
    {
        var clientOptions = ReadClientOptions();
        return Create(vpnAdapter, socketFactory, tracker, clientOptions);
    }

    public static VpnHoodClient Create(IVpnAdapter vpnAdapter, ISocketFactory socketFactory, ITracker? tracker, 
        ClientOptions clientOptions)
    {
        try {
            // delete the result file
            if (File.Exists(StatusFilePath))
                File.Delete(StatusFilePath);

            // create the client
            var client = new VpnHoodClient(vpnAdapter, socketFactory, tracker, clientOptions);
            return client;
        }
        catch (Exception ex) {
            SaveConnectionInfo(new ConnectionInfo {
                SessionInfo = null,
                SessionStatus = null,
                ApiEndPoint = null,
                ApiKey = null,
                ClientState = ClientState.None,
                Error = ex.ToApiError()
            });
            throw;
        }
    }

    internal static void SaveConnectionInfo(ConnectionInfo connectionInfo)
    {
        var json = JsonSerializer.Serialize(connectionInfo);
        File.WriteAllText(StatusFilePath, json);
    }
}