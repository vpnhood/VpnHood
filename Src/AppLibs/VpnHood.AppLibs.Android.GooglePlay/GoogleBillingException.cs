using Android.BillingClient.Api;

namespace VpnHood.AppLibs.Droid.GooglePlay;

public class GoogleBillingException : Exception
{
    private GoogleBillingException(string message) : base(message)
    {
    }

    public GoogleBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
    
    public static Exception Create(BillingResult billingResult, PurchaseState? purchaseState = null)
    {
        if (billingResult.ResponseCode == BillingResponseCode.Ok)
            throw new InvalidOperationException("Response code should not be OK.");

        return new GoogleBillingException(billingResult.DebugMessage) {
            Data = {
                { "BillingResponseCode", billingResult.ResponseCode.ToString() },
                { "BillingMessage", billingResult.DebugMessage },
                { "PurchaseState", purchaseState.ToString() }
            }
        };
    }
}