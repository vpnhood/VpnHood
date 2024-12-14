﻿using VpnHood.AppLibs.ClientProfiles;

namespace VpnHood.AppLibs.WebServer.Api;

public interface IClientProfileController
{
    Task<ClientProfileInfo> AddByAccessKey(string accessKey);
    Task<ClientProfileInfo> Get(Guid clientProfileId);
    Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams);
    Task Delete(Guid clientProfileId);
}