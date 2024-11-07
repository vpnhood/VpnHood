﻿namespace VpnHood.Client.App.Abstractions;

public interface IAppAccountProvider
{
    IAppAuthenticationProvider AuthenticationProvider { get; }
    IAppBillingProvider? BillingProvider { get; }
    Task<AppAccount?> GetAccount();
    Task<string[]> GetAccessKeys(string subscriptionId);
}