namespace VpnHood.AppLib.Api.VpnProfiles;

/// <summary>Represents what the person may do with a server location: its plans, as the server's policy and this build allow them.</summary>
public class ServerLocationOptions
{
    /// <summary>
    /// Gets or sets the minutes of an ordinary connection (<see cref="VpnHood.AppLib.Api.App.ConnectPlanId.Normal"/>), 0
    /// for no limit, or <see langword="null"/> where none is offered.
    /// </summary>
    public int? Normal { get; set; }

    /// <summary>Gets or sets the minutes of an ordinary connection after a rewarded ad, 0 for no limit, or <see langword="null"/> where none is offered.</summary>
    public int? NormalByRewardedAd { get; set; }

    /// <summary>Gets or sets the minutes of a premium trial, 0 for no limit, or <see langword="null"/> where none is offered.</summary>
    public int? PremiumByTrial { get; set; }

    /// <summary>Gets or sets the minutes of premium a rewarded ad gives, 0 for no limit, or <see langword="null"/> where none is offered.</summary>
    public int? PremiumByRewardedAd { get; set; }

    /// <summary>Gets or sets a value indicating whether premium can be bought here: the policy offers it, and this build has a store or may open the operator's shop.</summary>
    public bool PremiumByPurchase { get; set; }

    /// <summary>Gets or sets a value indicating whether premium can be had by a premium code here: the policy offers it, and this build takes typed codes.</summary>
    public bool PremiumByCode { get; set; }

    /// <summary>Gets or sets a value indicating whether premium can be bought or had by a code here.</summary>
    public bool CanGoPremium { get; set; }

    /// <summary>Gets or sets a value indicating whether the location has free servers.</summary>
    public bool HasFree { get; set; }

    /// <summary>Gets or sets a value indicating whether the location has premium servers.</summary>
    public bool HasPremium { get; set; }

    /// <summary>Gets or sets a value indicating whether the location has unblockable servers.</summary>
    public bool HasUnblockable { get; set; }

    /// <summary>Gets or sets a value indicating whether a connect to the location asks the person to choose a plan first: a trial or a rewarded ad is offered.</summary>
    public bool Prompt { get; set; }
}