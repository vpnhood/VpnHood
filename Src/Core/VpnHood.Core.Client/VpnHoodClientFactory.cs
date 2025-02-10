using System.Net;
using System.Text.Json;
using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client;

public class VpnHoodClientFactory
{
    private static string ConfigFilePath => Path.Combine(Directory.GetCurrentDirectory(), ClientOptions.VpnConfigFileName);
    private static string ConfigResultFilePath => Path.Combine(Directory.GetCurrentDirectory(), ClientOptions.VpnConfigResultFileName);

    public static VpnHoodClient Create(IVpnAdapter vpnAdapter, ISocketFactory socketFactory, ITracker? tracker)
    {
        try {
            // delete the result file
            if (File.Exists(ConfigResultFilePath))
                File.Delete(ConfigResultFilePath);

            // read from config file
            var json = File.ReadAllText(ConfigFilePath);
            var clientOptions = VhUtil.JsonDeserialize<ClientOptions>(json);

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
                ErrorCode = SessionErrorCode.GeneralError,
                Error = new ApiError(ex)
            });
            throw;
        }
    }

    internal static void SaveConnectionInfo(ConnectionInfo connectionInfo)
    {
        var json = JsonSerializer.Serialize(connectionInfo);
        File.WriteAllText(ConfigResultFilePath, json);
    }
}