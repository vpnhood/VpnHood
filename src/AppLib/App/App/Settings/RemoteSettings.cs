namespace VpnHood.AppLib.Settings;

public class RemoteSettings
{
    public bool ShowInternalAd { get; set; }
    public string? PromotionId { get; set; }
    public DateTime? PromotionStartDate { get; set; }
    public DateTime? PromotionEndDate { get; set; }
    public Uri? PromotionImageUrl { get; set; }
}