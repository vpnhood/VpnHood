namespace VpnHood.AccessServer.HostProvider.Ovh.Dto;

internal class IpConfigRequest
{
    // ReSharper disable InconsistentNaming
    public required string label { get; set; }
    public required string value { get; set; }
}