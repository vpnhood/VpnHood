using System;
using VpnHood.AccessServer.Dtos.AccessTokenDoms;

namespace VpnHood.AccessServer.Dtos;

public class AccessData 
{
    public Access Access { get; } 
    public AccessToken AccessToken { get; } 
    public Device? Device { get; } 
    public AccessData(Access access, AccessToken accessToken, Device? device)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        Device = device;
    }
}