using VpnHood.AccessServer.Dtos.AccessTokens;
using VpnHood.AccessServer.Dtos.Devices;

namespace VpnHood.AccessServer.Dtos;

public class AccessData(Access access, AccessToken accessToken, Device? device)
{
    public Access Access { get; } = access ?? throw new ArgumentNullException(nameof(access));
    public AccessToken AccessToken { get; } = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    public Device? Device { get; } = device;
}