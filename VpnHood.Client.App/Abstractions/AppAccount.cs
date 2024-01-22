namespace VpnHood.Client.App.Abstractions;

public class AppAccount
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string SubscriptionPlanId { get; set; }

}

public class AppProduct
{
    public required string ProductId { get; set; }
    public required string ProductName { get; set; }
    public required AppProductPlan[] Plans { get; set; }
}

public class AppProductPlan
{
    public required string PlanId { get; set; }
    public required decimal PriceAmount { get; set; }
    public required string PriceCurrency { get; set; }
}