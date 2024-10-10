using Android.BillingClient.Api;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
    
    public static Exception Create(BillingResult billingResult, PurchaseState? purchaseState = null)
    {
        if (billingResult.ResponseCode == BillingResponseCode.Ok)
            throw new InvalidOperationException("Response code should not be OK.");

        return new BillingException(billingResult.DebugMessage) {
            Data = {
                { "ResponseCode", billingResult.ResponseCode.ToString() },
                { "PurchaseState", purchaseState.ToString() }
            }
        };
    }
}