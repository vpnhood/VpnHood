﻿namespace VpnHood.AccessServer.Dtos.AccessTokenDtos;

public class AccessTokenData
{
    public AccessToken AccessToken { get; set; }
    public Usage? Usage { get; set; }
    public Access? Access { get; set; }

    public AccessTokenData(AccessToken accessToken)
    {
        AccessToken = accessToken;
    }
}