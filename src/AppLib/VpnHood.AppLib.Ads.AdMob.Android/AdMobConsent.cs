using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Xamarin.Google.UserMesssagingPlatform;

namespace VpnHood.AppLib.Ads.AdMob.Android;

// Google's consent form (UMP), where a law wants a yes before ads use personal data: the EEA, the UK
// and Switzerland, as Google tells from where the request comes. It is asked before AdMob's first
// start in each run of the app, as Google requires, and a preload only asks Google whether a form is
// due, so the form comes with an ad the person waits for, before that ad's timed load
// (AdMobConsentProvider). AdMob reads the answer itself, and a no still gets ads, limited ones. The
// form and its words are set in AdMob (Privacy & messaging); while none is published there, nothing
// shows.
internal static class AdMobConsent
{
    // Google's answer to whether a form is due; past this the next ad asks again, which a VPN that
    // has come up since may let through
    private static readonly TimeSpan InfoUpdateTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lock UpdateLock = new();
    private static readonly Lock GatherLock = new();
    private static Task? _updateTask;
    private static Task? _gatherTask;
    private static Activity? _gatherActivity;

    // Google's word on whether a form is due, asked once a run and shared like the gathering below
    public static Task Update(Activity activity, CancellationToken cancellationToken)
    {
        lock (UpdateLock) {
            if (_updateTask is null or { IsFaulted: true } or { IsCanceled: true })
                _updateTask = UpdateCore(activity);

            return _updateTask.WaitAsync(cancellationToken);
        }
    }

    // One gathering at a time, shared by every ad waiting for it, so a caller that gives up does not
    // take away a form the person is reading; one that failed is tried again by the next ad, and so is
    // one whose window was destroyed, as no answer may ever come to end it.
    public static Task Gather(Activity activity, CancellationToken cancellationToken)
    {
        lock (GatherLock) {
            if (_gatherTask is null or { IsFaulted: true } or { IsCanceled: true } ||
                (!_gatherTask.IsCompleted && _gatherActivity?.IsDestroyed == true)) {
                _gatherActivity = activity;
                _gatherTask = GatherCore(activity);
            }

            return _gatherTask.WaitAsync(cancellationToken);
        }
    }

    // The gathering, its failure logged: Google's rule is that an answer from an earlier run still
    // counts when this one cannot get one, so the caller goes on to CanRequestAds.
    public static async Task TryGather(Activity activity, CancellationToken cancellationToken)
    {
        try {
            await Gather(activity, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not gather the ad consent.");
        }
    }

    // the person has answered, or no answer is due where they are; an earlier run's answer counts
    // while this run cannot reach Google
    public static bool CanRequestAds =>
        UserMessagingPlatform.GetConsentInformation(Application.Context).CanRequestAds();

    // the person has answered a form that Google requires a way back to
    public static bool IsPrivacyOptionsRequired {
        get {
            var consentInformation = UserMessagingPlatform.GetConsentInformation(Application.Context);
            return consentInformation.ConsentStatus == ConsentInformationConsentStatus.Obtained &&
                   consentInformation.PrivacyOptionsRequirementStatus?
                       .Equals(ConsentInformationPrivacyOptionsRequirementStatus.Required) == true;
        }
    }

    public static async Task ShowPrivacyOptions(Activity activity, CancellationToken cancellationToken)
    {
        var dismissedListener = new FormDismissedListener();
        await AndroidUtils
            .RunOnUiThread(activity, () => UserMessagingPlatform.ShowPrivacyOptionsForm(activity, dismissedListener))
            .WaitAsync(cancellationToken)
            .Vhc();

        await dismissedListener.Task.WaitAsync(cancellationToken).Vhc();
    }

    private static async Task UpdateCore(Activity activity)
    {
        var consentInformation = UserMessagingPlatform.GetConsentInformation(activity);
        var updateListener = new InfoUpdateListener();
        var requestParameters = new ConsentRequestParameters.Builder().Build();
        await AndroidUtils
            .RunOnUiThread(activity, () => consentInformation.RequestConsentInfoUpdate(
                activity, requestParameters, updateListener, updateListener))
            .Vhc();

        await updateListener.Task.WaitAsync(InfoUpdateTimeout).Vhc();
    }

    private static async Task GatherCore(Activity activity)
    {
        await Update(activity, CancellationToken.None).Vhc();

        // the SDK shows the form only where one is due and not answered yet
        var dismissedListener = new FormDismissedListener();
        await AndroidUtils
            .RunOnUiThread(activity, () => UserMessagingPlatform.LoadAndShowConsentFormIfRequired(activity, dismissedListener))
            .Vhc();

        await dismissedListener.Task.Vhc();
    }

    private class InfoUpdateListener : Java.Lang.Object,
        IConsentInformationOnConsentInfoUpdateSuccessListener, IConsentInformationOnConsentInfoUpdateFailureListener
    {
        private readonly TaskCompletionSource _completionSource = new();
        public Task Task => _completionSource.Task;

        public void OnConsentInfoUpdateSuccess()
        {
            _completionSource.TrySetResult();
        }

        public void OnConsentInfoUpdateFailure(FormError formError)
        {
            _completionSource.TrySetException(
                new AdException($"Could not learn whether an ad consent is due. {formError.Message}"));
        }
    }

    private class FormDismissedListener : Java.Lang.Object, IConsentFormOnConsentFormDismissedListener
    {
        private readonly TaskCompletionSource _completionSource = new();
        public Task Task => _completionSource.Task;

        public void OnConsentFormDismissed(FormError? formError)
        {
            if (formError == null)
                _completionSource.TrySetResult();
            else
                _completionSource.TrySetException(new AdException($"The ad consent form failed. {formError.Message}"));
        }
    }
}
