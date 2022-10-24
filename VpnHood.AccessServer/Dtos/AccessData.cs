using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessData 
{
    public Access Access { get; } 
    public Models.AccessToken AccessToken { get; } 
    public Models.Device? Device { get; } 
    public AccessData(Access access, Models.AccessToken accessToken, Models.Device? device)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        Device = device;
    }
}