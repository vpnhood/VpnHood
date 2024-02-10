using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos.ServerProfile;

public class ServerProfileUpdateParams
{
    public Patch<string>? ServerProfileName { get; set; }
    public Patch<string?>? ServerConfig { get; set; }
}