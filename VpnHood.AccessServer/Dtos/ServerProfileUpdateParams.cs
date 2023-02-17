using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class ServerProfileUpdateParams
{
    public Patch<string?>? ServerConfig { get; set; }
}