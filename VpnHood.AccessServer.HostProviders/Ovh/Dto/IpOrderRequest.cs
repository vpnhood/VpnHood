namespace VpnHood.AccessServer.HostProvider.Ovh.Dto;

internal class IpOrderRequest
{
    // ReSharper disable InconsistentNaming
    public required string duration { get; set; }
    public required string planCode { get; set; }
    public required string pricingMode { get; set; }
    public required int quantity { get; set; }
}