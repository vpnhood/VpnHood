namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

internal class CheckoutRequest
{
    // ReSharper disable InconsistentNaming
    public required bool autoPayWithPreferredPaymentMethod { get; set; }
    // ReSharper disable once IdentifierTypo
    public required bool waiveRetractationPeriod { get; set; }
}