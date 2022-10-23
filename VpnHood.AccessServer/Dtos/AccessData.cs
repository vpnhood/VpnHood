using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class AccessData 
{
    public Access Access { get; } 
    public Models.AccessToken AccessToken { get; } 
    public Models.Device? Device { get; } 
    public AccessData(Access access, AccessToken accessToken, Device? device)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        Device = device;
    }
}