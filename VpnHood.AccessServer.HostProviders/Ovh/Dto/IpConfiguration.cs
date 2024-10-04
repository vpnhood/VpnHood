namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

internal class IpConfiguration
{
    public required int Id { get; set; }
    public required string Label { get; set; }
    public required string Value { get; set; }
}