namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

internal class IpData
{
    public required string Ip { get; set; }
    public bool IsAdditionalIp { get; set; }
    public List<string>? Regions { get; set; }
    public required string Description { get; set; }
    public bool CanBeTerminated { get; set; }
    public string? Campus { get; set; }
    public required string Type { get; set; }
    public string? Country { get; set; }
    public required RoutedTo RoutedTo { get; set; }
}