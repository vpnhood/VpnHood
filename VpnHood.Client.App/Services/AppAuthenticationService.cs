﻿using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Services;

internal class AppAuthenticationService(VpnHoodApp vpnHoodApp, IAppAuthenticationService accountService)
    : IAppAuthenticationService
{
    public bool IsSignInWithGoogleSupported => accountService.IsSignInWithGoogleSupported;
    public string? UserId => accountService.UserId;
    public HttpClient HttpClient => accountService.HttpClient;

    public async Task SignInWithGoogle(IAppUiContext uiContext)
    {
        await accountService.SignInWithGoogle(uiContext);
        await vpnHoodApp.RefreshAccount();
    }

    public async Task SignOut(IAppUiContext uiContext)
    {
        await accountService.SignOut(uiContext);
        await vpnHoodApp.RefreshAccount();
    }

    public void Dispose()
    {
        accountService.Dispose();
    }
}