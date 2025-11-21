namespace VpnHood.AppLib.Settings;

// ReSharper disable once ClassNeverInstantiated.Global : is used in deserialization
public class RemoteSettings
{
    public bool ShowInternalAd { get; set; }
    public string? PromotionId { get; set; }
    public DateTime? PromotionEndDate { get; set; }
    public Uri? PromotionImageUrl { get; set; }
}