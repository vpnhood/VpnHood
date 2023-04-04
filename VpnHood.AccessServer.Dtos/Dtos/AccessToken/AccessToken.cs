using System;

namespace VpnHood.AccessServer.Dtos;

public class AccessToken
{
    public Guid ProjectId { get; set; }
    public Guid AccessTokenId { get; set; }
    public string? AccessTokenName { get; set; }
    public int SupportCode { get; set; }
    public Guid ServerFarmId { get; set; }
    public string? ServerFarmName { get; set; }
    public long MaxTraffic { get; set; }
    public int Lifetime { get; set; }
    public int MaxDevice { get; set; }
    public DateTime? FirstUsedTime { get; set; }
    public DateTime? LastUsedTime { get; set; }
    public string? Url { get; set; }
    public bool IsPublic { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime ModifiedTime { get; set; }
}