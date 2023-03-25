using System;

namespace VpnHood.AccessServer.Dtos.AccessTokenDoms;

public class AccessTokenCreateParams
{
    public Guid? AccessTokenId { get; set; }
    public Guid ServerFarmId { get; set; }
    public string? AccessTokenName { get; set; }
    public byte[]? Secret { get; set; }
    public long MaxTraffic { get; set; }
    public int Lifetime { get; set; }
    public int MaxDevice { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public string? Url { get; set; }
    public bool IsPublic { get; set; }
}