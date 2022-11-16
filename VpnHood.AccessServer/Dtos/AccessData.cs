using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class AccessData 
{
    public Access Access { get; } 
    public AccessTokenModel AccessTokenModel { get; } 
    public DeviceModel? Device { get; } 
    public AccessData(Access access, AccessTokenModel accessTokenModel, DeviceModel? device)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        AccessTokenModel = accessTokenModel ?? throw new ArgumentNullException(nameof(accessTokenModel));
        Device = device;
    }
}