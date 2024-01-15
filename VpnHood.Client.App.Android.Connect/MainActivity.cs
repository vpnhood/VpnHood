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

[IntentFilter([Intent.ActionMain], Categories = new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })]
[IntentFilter([TileService.ActionQsTilePreferences])]

public class MainActivity : AndroidAppWebViewMainActivity
{
    private GoogleSignInOptions _googleSignInOptions = default!;
    //private GoogleApiClient _googleApiClient = default!;
    private BillingClient _billingClient = default!;
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MobileAds.Initialize(this);

        // Set our simple view
        /*var linearLayout = new LinearLayout(this);

        _connectButton = new Button(this);
        _connectButton.Click += ConnectButton_Click;
        _connectButton.Text += "Test";
        linearLayout.AddView(_connectButton);

        _statusTextView = new TextView(this);
        linearLayout.AddView(_statusTextView);
        SetContentView(linearLayout);*/

        // Signin with Google
        /*_googleSignInOptions = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken("147087744118-asq131v41mqrt777frtghbv66u5u4d2d.apps.googleusercontent.com")
            .RequestEmail()
            .Build();
        _googleApiClient = new GoogleApiClient.Builder(this)
            .AddApi(Auth.GOOGLE_SIGN_IN_API, _googleSignInOptions).Build();
        _googleApiClient.Connect();*/


        // Google play billing
        var billingBuilder = BillingClient.NewBuilder(this);
        billingBuilder.SetListener(PurchasesUpdatedListener);
        billingBuilder.EnablePendingPurchases();
        _billingClient = billingBuilder.Build();
        StartConnection();
    }

    private void StartConnection()
    {
        _billingClient.StartConnection(OnBillingSetupFinished, OnBillingServiceDisconnected);
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

    private async void OnBillingSetupFinished(BillingResult billingResult)
    {
        if (billingResult.ResponseCode == BillingResponseCode.Ok)
        {
            var productDetailsParams = QueryProductDetailsParams.NewBuilder()
                .SetProductList([
                    QueryProductDetailsParams.Product.NewBuilder()
                        .SetProductId("hh")
                        .SetProductType(BillingClient.ProductType.Subs)
                        .Build()
                ]).Build();

            try
            {
                var productsDetails = await _billingClient.QueryProductDetailsAsync(productDetailsParams);
                OnProductDetailsResponse(productsDetails.Result, productsDetails.ProductDetails);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }


        }
    }

    private void OnBillingServiceDisconnected()
    {
        StartConnection();
    }

    private void OnProductDetailsResponse(BillingResult billingResult, IList<ProductDetails> productDetailsList)
    {
        if (productDetailsList.Count == 0)
            throw new NotImplementedException();

        var isDeviceSupportSubscription = _billingClient.IsFeatureSupported("subscriptions");
        if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
            throw new NotImplementedException();

        Console.WriteLine(productDetailsList);
        Console.WriteLine(billingResult);
    }



    // Google signin button
    /*[Obsolete("Obsolete")]
    private void ConnectButton_Click(object? sender, EventArgs e)
    {
        _statusTextView.Text = "Signin with Google";
        var intent = Auth.GoogleSignInApi.GetSignInIntent(_googleApiClient);
        StartActivityForResult(intent, 1);
    }*/

    // Google signin result
    /*protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == 1)
        {
            GoogleSignInResult result = Auth.GoogleSignInApi.GetSignInResultFromIntent(data);

            if (result.IsSuccess)
            {
                _statusTextView.Text = "Id Token: " + result.SignInAccount.IdToken;
            }
        }
    }*/

}