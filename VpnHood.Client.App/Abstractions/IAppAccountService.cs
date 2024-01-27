namespace VpnHood.Client.App.Abstractions;

public interface IAppAccountService
{
    bool IsGoogleSignInSupported { get; }
    Task SignInWithGoogle();
    Task SignOut();
    Task<AppAccount> GetAccount();
}