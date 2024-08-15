namespace VpnHood.AccessServer.HostProvider.Ovh.Dto;

internal class CheckoutRequest
{
    // ReSharper disable InconsistentNaming
    public required bool autoPayWithPreferredPaymentMethod { get; set; }
    public required bool waiveRetractationPeriod { get; set; }
}