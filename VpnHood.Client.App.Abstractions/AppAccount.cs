namespace VpnHood.Client.App.Abstractions;

public class AppAccount
{
    public required string UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ProviderPlanId { get; set; }
    public DateTime? CreatedTime { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? PriceCurrency { get; set; }
    public bool? IsAutoRenew { get; set; }
    public string? ProviderSubscriptionId { get; set; }
}