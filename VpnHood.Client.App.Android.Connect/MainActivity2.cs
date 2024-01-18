using Android.BillingClient.Api;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Ads;
using Android.Gms.Auth.Api.SignIn;
using Android.Service.QuickSettings;
using Android.Views;
using VpnHood.Client.App.Droid.Common.Activities;

namespace VpnHood.Client.App.Droid.Connect;

[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]

public class MainActivity : AndroidAppWebViewMainActivity
{
    private GoogleSignInOptions _googleSignInOptions = default!;
    private GoogleSignInClient _googleSignInClient = default!;
    private BillingClient _billingClient = default!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MobileAds.Initialize(this);

        // Signin with Google
        _googleSignInOptions = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken("147087744118-asq131v41mqrt777frtghbv66u5u4d2d.apps.googleusercontent.com")
            .RequestEmail()
            .Build();
        _googleSignInClient = GoogleSignIn.GetClient(this, _googleSignInOptions);

        // Google play billing
        var billingBuilder = BillingClient.NewBuilder(this);
        billingBuilder.SetListener(PurchasesUpdatedListener);
        billingBuilder.EnablePendingPurchases();
        _billingClient = billingBuilder.Build();
        StartConnection();
    }

    private async void StartConnection()
    {
        //_billingClient.StartConnection(OnBillingSetupFinished, OnBillingServiceDisconnected);
        var billingResult = await _billingClient.StartConnectionAsync();
        switch (billingResult.ResponseCode)
        {
            case BillingResponseCode.ServiceDisconnected:
                throw new NotImplementedException();
                break;
            case BillingResponseCode.Ok:
                OnBillingSetupFinished();
                break;
            case BillingResponseCode.BillingUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.DeveloperError:
                throw new NotImplementedException();
            case BillingResponseCode.Error:
                throw new NotImplementedException();
            case BillingResponseCode.FeatureNotSupported:
                throw new NotImplementedException();
            case BillingResponseCode.ItemAlreadyOwned:
                throw new NotImplementedException();
            case BillingResponseCode.ItemNotOwned:
                throw new NotImplementedException();
            case BillingResponseCode.ItemUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.NetworkError:
                throw new NotImplementedException();
            case BillingResponseCode.ServiceTimeout:
                throw new NotImplementedException();
            case BillingResponseCode.ServiceUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.UserCancelled:
                throw new NotImplementedException();
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async void OnBillingSetupFinished(BillingResult? billingResult = null)
    {
        var isDeviceSupportSubscription = _billingClient.IsFeatureSupported("subscriptions");
        if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
            throw new NotImplementedException();

        var productDetailsParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList([
                QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId("hidden_farm")
                    .SetProductType(BillingClient.ProductType.Subs)
                    .Build()
            ]).Build();

        try
        {
            var productsDetails = await _billingClient.QueryProductDetailsAsync(productDetailsParams);
            if (productsDetails.ProductDetails.Count > 0)
            {
                OnProductDetailsResponse(productsDetails.Result, productsDetails.ProductDetails);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    private void OnProductDetailsResponse(BillingResult billingResult, IList<ProductDetails> productDetailsList)
    {

        var isDeviceSupportSubscription = _billingClient.IsFeatureSupported("subscriptions");
        if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
            throw new NotImplementedException();

        Console.WriteLine(productDetailsList);
        Console.WriteLine(billingResult);
    }

    private void PurchasesUpdatedListener(BillingResult billingResult, IList<Purchase> purchases)
    {
        switch (billingResult.ResponseCode)
        {
            case BillingResponseCode.ServiceDisconnected:
                OnBillingServiceDisconnected();
                break;
            case BillingResponseCode.Ok:
                OnBillingSetupFinished(billingResult);
                break;
            case BillingResponseCode.BillingUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.DeveloperError:
                throw new NotImplementedException();
            case BillingResponseCode.Error:
                throw new NotImplementedException();
            case BillingResponseCode.FeatureNotSupported:
                throw new NotImplementedException();
            case BillingResponseCode.ItemAlreadyOwned:
                throw new NotImplementedException();
            case BillingResponseCode.ItemNotOwned:
                throw new NotImplementedException();
            case BillingResponseCode.ItemUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.NetworkError:
                throw new NotImplementedException();
            case BillingResponseCode.ServiceTimeout:
                throw new NotImplementedException();
            case BillingResponseCode.ServiceUnavailable:
                throw new NotImplementedException();
            case BillingResponseCode.UserCancelled:
                throw new NotImplementedException();
            default:
                throw new ArgumentOutOfRangeException();
        }
    }


    private void OnBillingServiceDisconnected()
    {
        StartConnection();
    }



    // Google signin button

    private void ConnectButton_Click(object? sender, EventArgs e)
    {
        _statusTextView.Text = "Signin with Google";
        var intent = _googleSignInClient.SignInIntent;
        StartActivityForResult(intent, 1);
    }

    // Google signin result
    protected override async void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == 1)
        {
            var account = await GoogleSignIn.GetSignedInAccountFromIntentAsync(data);

            if (account.IdToken != null)
            {
                _statusTextView.Text = "Id Token: " + account.IdToken;
            }
        }
    }

}