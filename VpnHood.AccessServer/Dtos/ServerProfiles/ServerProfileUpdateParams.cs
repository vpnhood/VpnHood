using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.ServerProfiles;

public class ServerProfileUpdateParams
{
    public Patch<string>? ServerProfileName { get; set; }
    public Patch<string?>? ServerConfig { get; set; }
}