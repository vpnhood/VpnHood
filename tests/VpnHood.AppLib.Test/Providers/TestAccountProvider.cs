using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAccountProvider : IAppAccountProvider
{
    public IAppAuthenticationProvider AuthenticationProvider { get; } = new TestAuthenticationProvider();
    public AppBilling? Billing { get; } = new() {
        Provider = new TestBillingProvider(),
        OrderProcessor = new TestOrderProcessor()
    };

    public Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        return Task.FromResult<AppAccount?>(null);
    }

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(string.Empty);
    }
}
