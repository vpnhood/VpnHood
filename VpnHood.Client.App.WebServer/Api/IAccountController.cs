using System.Diagnostics.CodeAnalysis;
using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.WebServer.Api;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle();
    Task SignOut();
    Task Refresh();
    Task<AppAccount?> Get();
    Task<bool> IsSubscriptionOrderProcessed(string providerOrderId);
    Task<List<string>> GetAccessKeys(string subscriptionId);
}