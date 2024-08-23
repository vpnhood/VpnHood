namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

public class OvhHostProviderSettings
{
    public required string EndPoint { get; set; }
    public required string ApplicationKey { get; set; }
    public required string ApplicationSecret { get; set; }
    public required string ConsumerKey { get; set; }
    public required string OvhSubsidiary { get; set; }
}