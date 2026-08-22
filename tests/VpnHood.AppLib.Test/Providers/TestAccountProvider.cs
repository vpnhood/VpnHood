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
        // typing a code is saying "use this", so it is also the whole of Retry: writing a code puts
        // it back in the ranking (keyring plan §4)
        if (accessCode != null)
            RejectedCodes.Remove(accessCode);
        RankAccessCode(account);
        return Task.CompletedTask;
    }

    /// <summary>The codes this account has had refused, as the portal keeps them: per account, and
    /// covering every entry holding that string, because identical codes are the same credential.</summary>
    public HashSet<string> RejectedCodes { get; } = [];

    public List<string> ReportedRejections { get; } = [];

    /// <summary>
    /// Stands in for the portal's eligibility bit (keyring plan §4). Applied only while the report
    /// is still about the account's CURRENT code, so a refusal overtaken by a different code does
    /// nothing at all.
    /// </summary>
    public Task ReportAccessCodeRejected(string accessCode, CancellationToken cancellationToken)
    {
        ReportedRejections.Add(accessCode);

        if (Account is { } account && account.AccessCodeInfo?.AccessCode == accessCode) {
            RejectedCodes.Add(accessCode);
            RankAccessCode(account);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// What the account serves: a store subscription's own code first, else the upload — refused or
    /// not. A refusal DEMOTES a code behind everything else the account holds, but never takes it
    /// away (keyring plan §4), and this account holds exactly one, so its turn comes round every
    /// time. Deterministic, with no dates in it (§2).
    /// </summary>
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
