using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;

namespace VpnHood.App.AvaloniaUI.Dev;

// A store build's account without the portal ("--store"): the signed-in sample person, the plans the
// store prices, and a purchase that ends in a sample subscription on the account. It is its own order
// processor, as the portal is for a real build.
internal sealed class DevAccountProvider : IAccountProvider, IOrderProcessor
{
    private readonly DevAuthenticationProvider _authentication;
    private readonly string _storeId;
    private readonly bool _isSubscriptionManagementSupported;
    private Account? _account;

    public DevAccountProvider(string storeId, string storeAuthProviderId, bool isSubscriptionManagementSupported)
    {
        _storeId = storeId;
        _isSubscriptionManagementSupported = isSubscriptionManagementSupported;
        _authentication = new DevAuthenticationProvider(storeAuthProviderId);
        Billing = new AppBilling {
            Provider = new DevBillingProvider(storeId, isSubscriptionManagementSupported),
            OrderProcessor = this
        };
    }

    public IAuthenticationProvider AuthenticationProvider => _authentication;
    public AppBilling? Billing { get; }

    public Task<Account?> GetAccount(CancellationToken cancellationToken)
    {
        return Task.FromResult(SignedInAccount());
    }

    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return Task.FromResult(DevBillingProvider.ProductIds);
    }

    public Task SetAccessCode(string? accessCode, CancellationToken cancellationToken)
    {
        var account = SignedInAccount() ?? throw new InvalidOperationException("There is no account to upload to.");
        account.AccessCodeInfo = accessCode == null ? null : new AccessCodeInfo { AccessCode = accessCode };
        return Task.CompletedTask;
    }

    public Task ReportAccessCodeRejected(string accessCode, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DeleteAccount(CancellationToken cancellationToken)
    {
        _account = null;
        return Task.CompletedTask;
    }

    public Task<PurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        var userId = _authentication.UserId ?? throw new InvalidOperationException("Sign in before buying.");
        return Task.FromResult(new PurchaseAttribution { UserId = userId });
    }

    public Task CompleteOrder(PurchaseProof purchaseProof, CancellationToken cancellationToken)
    {
        var account = SignedInAccount() ?? throw new InvalidOperationException("There is no account to complete the order for.");
        var isYearly = purchaseProof.Value == "dev_yearly";
        account.Subscription = new Subscription {
            StoreId = _storeId,
            CreatedTime = DateTime.UtcNow,
            ExpirationTime = isYearly ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
            PriceAmount = isYearly ? 39.99m : 4.99m,
            PriceCurrency = "USD",
            BillingPeriod = isYearly ? "P1Y" : "P1M",
            IsAutoRenew = true,
            Management = _isSubscriptionManagementSupported
                ? SubscriptionManagement.Available
                : SubscriptionManagement.NotOnThisDevice
        };
        return Task.CompletedTask;
    }

    // the portal answers for the signed-in person only: a sign-out drops the account, and the next
    // sign-in starts a fresh one
    private Account? SignedInAccount()
    {
        if (_authentication.UserId is not { } userId) {
            _account = null;
            return null;
        }

        _account ??= new Account { UserId = userId, Name = "Sample Person", Email = "sample@example.com" };
        return _account;
    }
}
