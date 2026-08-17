using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAccountProvider : IAccountProvider
{
    public Account? Account { get; set; }
    public int DeleteAccountCalls { get; private set; }

    public int GetAccountCalls { get; private set; }
    public IReadOnlyList<string> ProductIds { get; set; } = ["test_plan_1m"];
    public TestAuthenticationProvider TestAuthenticationProvider { get; } = new();
    public TestBillingProvider TestBillingProvider { get; } = new();
    public TestOrderProcessor TestOrderProcessor { get; } = new();

    public IAuthenticationProvider AuthenticationProvider => TestAuthenticationProvider;

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

    public Task<Account?> GetAccount(CancellationToken cancellationToken)
    {
        GetAccountCalls++;
        // an account provider answers for the SIGNED-IN person: no session, no account
        // (PortalAccountProvider.GetAccount returns null the same way)
        return Task.FromResult(TestAuthenticationProvider.UserId == null ? null : Account);
    }

    /// <summary>Set to make the backend refuse the deletion (the portal's "deletion_blocked").</summary>
    public Exception? DeleteAccountException { get; set; }

    public Task DeleteAccount(CancellationToken cancellationToken)
    {
        DeleteAccountCalls++;
        if (DeleteAccountException != null)
            return Task.FromException(DeleteAccountException);

        Account = null;
        return Task.CompletedTask;
    }
}
