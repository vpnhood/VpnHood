namespace VpnHood.Client.App.Accounts;

public interface IAccountService
{
    bool IsGoogleSignInSupported { get; }
    Task SignInWithGoogle();
    Task<Account> GetAccount();
}