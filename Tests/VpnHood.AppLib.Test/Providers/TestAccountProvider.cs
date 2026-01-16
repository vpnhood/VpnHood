using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAccountProvider : IAppAccountProvider
{
    public IAppAuthenticationProvider AuthenticationProvider { get; } = new TestAuthenticationProvider();
    public IAppBillingProvider? BillingProvider { get; } = new TestBillingProvider();

    public Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        return Task.FromResult<AppAccount?>(null);
    }

    public Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Array.Empty<string>());
    }
}