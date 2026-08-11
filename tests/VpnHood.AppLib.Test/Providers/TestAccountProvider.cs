using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAccountProvider : IAppAccountProvider
{
    public AppAccount? Account { get; set; }
    public int DeleteAccountCalls { get; private set; }
    public IReadOnlyList<string> ProductIds { get; set; } = ["test_plan_1m"];
    public TestAuthenticationProvider TestAuthenticationProvider { get; } = new();
    public TestBillingProvider TestBillingProvider { get; } = new();
    public TestOrderProcessor TestOrderProcessor { get; } = new();

    public IAppAuthenticationProvider AuthenticationProvider => TestAuthenticationProvider;

    public AppBilling? Billing { get; }

    public TestAccountProvider()
    {
        Billing = new AppBilling {
            Provider = TestBillingProvider,
            OrderProcessor = TestOrderProcessor
        };
    }

    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return Task.FromResult(ProductIds);
    }

    public Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        return Task.FromResult(Account);
    }

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(string.Empty);
    }

    public Task DeleteAccount(IUiContext uiContext, CancellationToken cancellationToken)
    {
        DeleteAccountCalls++;
        Account = null;
        return Task.CompletedTask;
    }
}
