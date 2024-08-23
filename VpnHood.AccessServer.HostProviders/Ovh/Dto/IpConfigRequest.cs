namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

internal class IpConfigRequest
{
    // ReSharper disable InconsistentNaming
    public required string label { get; set; }
    public required string value { get; set; }
}