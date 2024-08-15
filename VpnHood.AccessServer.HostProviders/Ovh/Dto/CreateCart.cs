namespace VpnHood.AccessServer.HostProvider.Ovh.Dto;

internal class RequestBodyForCreateCart
{
    // ReSharper disable InconsistentNaming
    public required string description { get; set; }
    public required DateTime expire { get; set; }
    public required string ovhSubsidiary { get; set; }
}