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

    public List<string?> SetAccessCodeCalls { get; } = [];

    /// <summary>Set to make the upload fail the way a network outage does.</summary>
    public Exception? SetAccessCodeException { get; set; }

    /// <summary>The uploaded code, as the portal's single slot would hold it.</summary>
    public string? UploadedAccessCode { get; private set; }

    /// <summary>
    /// Stands in for the portal's slot and ranking (keyring plan §2, §5). Nothing is ordered by time:
    /// whichever upload arrives last wins, which is the whole protocol.
    /// </summary>
    public Task SetAccessCode(string? accessCode, CancellationToken cancellationToken)
    {
        SetAccessCodeCalls.Add(accessCode);
        if (SetAccessCodeException != null)
            return Task.FromException(SetAccessCodeException);

        var account = Account ?? throw new InvalidOperationException("There is no account to upload to.");
        UploadedAccessCode = accessCode;
        RankAccessCode(account);
        return Task.CompletedTask;
    }

    public List<(string AccessCode, DateTime ExpirationTime)> ReportedExpirations { get; } = [];

    public Task ReportAccessCodeExpiration(string accessCode, DateTime expirationTime,
        CancellationToken cancellationToken)
    {
        ReportedExpirations.Add((accessCode, expirationTime));

        // an expiry the account already knows to be past takes the code out of the ranking, and the
        // uploaded code is the only one this fake holds
        if (Account is { } account && UploadedAccessCode == accessCode)
            RankAccessCode(account);

        return Task.CompletedTask;
    }

    /// <summary>What the account serves: a store subscription's own code first, else the upload.</summary>
    private void RankAccessCode(Account account)
    {
        if (account.Subscription != null)
            return;

        account.AccessCodeInfo = UploadedAccessCode == null
            ? null
            : new AccessCodeInfo { AccessCode = UploadedAccessCode };
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
