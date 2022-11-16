﻿using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessData 
{
    public Access Access { get; } 
    public Models.AccessTokenModel AccessTokenModel { get; } 
    public Models.DeviceModel? Device { get; } 
    public AccessData(Access access, Models.AccessTokenModel accessTokenModel, Models.DeviceModel? device)
    {
        Access = access ?? throw new ArgumentNullException(nameof(access));
        AccessTokenModel = accessTokenModel ?? throw new ArgumentNullException(nameof(accessTokenModel));
        Device = device;
    }
}