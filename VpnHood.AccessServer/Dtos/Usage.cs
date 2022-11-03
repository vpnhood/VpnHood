using System;

namespace VpnHood.AccessServer.Dtos;

public class Usage
{
    public long SentTraffic { get; set; }
    public long ReceivedTraffic { get; set; }
    public int? DeviceCount { get; set; }
    public int? ServerCount { get; set; }
    public int? SessionCount { get; set; }
    public int? AccessTokenCount { get; set; }
    public int? CountryCount { get; set; }
}