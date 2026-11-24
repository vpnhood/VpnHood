namespace VpnHood.AppLogic.App.Settings;

internal class RemoteSettings
{
    public bool ShowInternalAd { get; set; }
    public string[]? OverVpnAdProviders { get; set; } // in place of the head's marks (AppAdProviderItem.CanShowOverVpn)
    public string? PromotionId { get; set; }
    public DateTime? PromotionStartDate { get; set; }
    public DateTime? PromotionEndDate { get; set; }
    public Uri? PromotionImageUrl { get; set; }
}