namespace VpnHood.AppLib.ClientProfiles;

public class ServerLocationOptions
{
    public int? Normal { get; set; }
    public int? PremiumByTrial { get; set; }
    public int? PremiumByRewardedAd { get; set; }
    public bool PremiumByPurchase { get; set; }
    public bool HasFree { get; set; }
    public bool HasPremium { get; set; }
    public bool HasUnblockable { get; set; }
    public bool Prompt { get; set; }
}
