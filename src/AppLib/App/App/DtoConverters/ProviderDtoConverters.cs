using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.Api.Device;
using ProviderAccounts = VpnHood.AppLib.Abstractions.Accounts;
using ProviderAds = VpnHood.AppLib.Abstractions.Ads;
using ProviderBilling = VpnHood.AppLib.Abstractions.Billing;
using ProviderDevice = VpnHood.AppLib.Abstractions.Device;

namespace VpnHood.AppLib.DtoConverters;

// The provider surface and the contract say the same things in two vocabularies, and this is where
// they meet. The duplication is the point: a head's account, billing, ad or device-UI provider
// compiles against VpnHood.AppLib.Abstractions and never sees the app's published data model, so a
// field added to the contract for a UI cannot reach a third party's provider package, and a
// provider package does not rebuild when the contract moves.
//
// Every enum maps with no default arm - a member added on either side breaks this file rather than
// arriving somewhere as a silent fallback. The classes have no such guard: a property added to one
// side and forgotten here reaches the UI as a blank, so add the pair in one edit.
public static class ProviderDtoConverters
{
    public static Account ToAppDto(this ProviderAccounts.Account account)
    {
        return new Account {
            UserId = account.UserId,
            Name = account.Name,
            Email = account.Email,
            Subscription = account.Subscription?.ToAppDto(),
            AccessCodeInfo = account.AccessCodeInfo?.ToAppDto()
        };
    }

    public static AccessCodeInfo ToAppDto(this ProviderAccounts.AccessCodeInfo accessCodeInfo)
    {
        return new AccessCodeInfo {
            AccessCode = accessCodeInfo.AccessCode,
            ExpirationTime = accessCodeInfo.ExpirationTime
        };
    }

    public static Subscription ToAppDto(this ProviderAccounts.Subscription subscription)
    {
        return new Subscription {
            StoreId = subscription.StoreId,
            CreatedTime = subscription.CreatedTime,
            ExpirationTime = subscription.ExpirationTime,
            PriceAmount = subscription.PriceAmount,
            PriceCurrency = subscription.PriceCurrency,
            BillingPeriod = subscription.BillingPeriod,
            IsAutoRenew = subscription.IsAutoRenew,
            Management = subscription.Management.ToAppDto()
        };
    }

    public static SubscriptionManagement ToAppDto(this ProviderAccounts.SubscriptionManagement management)
    {
        return management switch {
            ProviderAccounts.SubscriptionManagement.AnotherStore => SubscriptionManagement.AnotherStore,
            ProviderAccounts.SubscriptionManagement.NotOnThisDevice => SubscriptionManagement.NotOnThisDevice,
            ProviderAccounts.SubscriptionManagement.Available => SubscriptionManagement.Available
        };
    }

    // Inbound: what a UI asked for, handed to the authentication provider.
    public static ProviderAccounts.SignInOptions ToProvider(this SignInOptions signInOptions)
    {
        return new ProviderAccounts.SignInOptions {
            ProviderId = signInOptions.ProviderId,
            UserName = signInOptions.UserName,
            Password = signInOptions.Password,
            TwoFactorCode = signInOptions.TwoFactorCode
        };
    }

    public static SignInResult ToAppDto(this ProviderAccounts.SignInResult signInResult)
    {
        return new SignInResult {
            State = signInResult.State.ToAppDto(),
            NewBackupCode = signInResult.NewBackupCode
        };
    }

    public static SignInState ToAppDto(this ProviderAccounts.SignInState state)
    {
        return state switch {
            ProviderAccounts.SignInState.SignedIn => SignInState.SignedIn,
            ProviderAccounts.SignInState.TotpRequired => SignInState.TotpRequired
        };
    }

    public static SubscriptionPlan ToAppDto(this ProviderBilling.SubscriptionPlan plan)
    {
        return new SubscriptionPlan {
            BasePrice = plan.BasePrice,
            CurrentPrice = plan.CurrentPrice,
            Period = plan.Period,
            TrialPeriod = plan.TrialPeriod,
            PlanToken = plan.PlanToken,
            CurrencySymbol = plan.CurrencySymbol,
            CurrencyCode = plan.CurrencyCode
        };
    }

    public static PurchaseState ToAppDto(this ProviderBilling.PurchaseState purchaseState)
    {
        return purchaseState switch {
            ProviderBilling.PurchaseState.None => PurchaseState.None,
            ProviderBilling.PurchaseState.Started => PurchaseState.Started,
            ProviderBilling.PurchaseState.Processing => PurchaseState.Processing
        };
    }

    // Inbound: the plan a UI picked, handed to the store.
    public static ProviderBilling.PurchaseParams ToProvider(this PurchaseParams purchaseParams)
    {
        return new ProviderBilling.PurchaseParams {
            PlanToken = purchaseParams.PlanToken
        };
    }

    public static ShowAdResult ToAppDto(this ProviderAds.ShowAdResult result)
    {
        return result switch {
            ProviderAds.ShowAdResult.Closed => ShowAdResult.Closed,
            ProviderAds.ShowAdResult.Clicked => ShowAdResult.Clicked
        };
    }

    // Inbound: how the UI's own internal ad ended, told to the ad service.
    public static ProviderAds.ShowAdResult ToProvider(this ShowAdResult result)
    {
        return result switch {
            ShowAdResult.Closed => ProviderAds.ShowAdResult.Closed,
            ShowAdResult.Clicked => ProviderAds.ShowAdResult.Clicked
        };
    }

    public static PrivateDns ToAppDto(this ProviderDevice.PrivateDns privateDns)
    {
        return new PrivateDns {
            IsActive = privateDns.IsActive,
            Provider = privateDns.Provider
        };
    }

    public static SystemBarsInfo ToAppDto(this ProviderDevice.SystemBarsInfo systemBarsInfo)
    {
        return new SystemBarsInfo {
            TopHeight = systemBarsInfo.TopHeight,
            BottomHeight = systemBarsInfo.BottomHeight
        };
    }
}
