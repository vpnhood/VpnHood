using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;
// this file sits in a namespace called Avalonia, so the framework type needs an explicit name
using AvaloniaApplication = Avalonia.Application;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppLib.App.Android.Activities;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppUi.Hosting.Avalonia.Android;

/// <summary>
/// Represents the Avalonia UI's main activity on Android: Avalonia's main activity, raising the app's activity events
/// itself and leaving the rest to the app's main activity handler.
/// </summary>
/// <remarks>
/// The handler gives the app the UI context it needs to ask for VPN permission, and takes the access-key intents and the
/// activity results. The Back key, a remote's or a phone's, reaches Avalonia, which the UI answers. On a phone the
/// activity requests portrait, as the UI's wide layout is for tablets, TVs and windows; it asks at run time, not in the
/// manifest, whose portrait would make Google Play hide the app from TVs.
/// </remarks>
/// <typeparam name="TUi">The UI's application.</typeparam>
public class AndroidAvaloniaMainActivity<TUi> : AvaloniaMainActivity, IActivityEvent
    where TUi : AvaloniaApplication, IAvaloniaUi
{
    private AndroidAppMainActivityHandler? MainActivityHandler { get; set; }

    /// <inheritdoc/>
    public event EventHandler<ActivityResultEventArgs>? ActivityResultEvent;
    /// <inheritdoc/>
    public event EventHandler<CreateEventArgs>? CreateEvent;
    /// <inheritdoc/>
    public event EventHandler<NewIntentEventArgs>? NewIntentEvent;
    /// <inheritdoc/>
    public event EventHandler<RequestPermissionsResultArgs>? RequestPermissionsResultEvent;
    /// <inheritdoc/>
    public event EventHandler<KeyDownArgs>? KeyDownEvent;
    /// <inheritdoc/>
    public event EventHandler<Configuration>? ConfigurationChangedEvent;
    /// <inheritdoc/>
    public event EventHandler? PauseEvent;
    /// <inheritdoc/>
    public event EventHandler? ResumeEvent;
    /// <inheritdoc/>
    public event EventHandler? DestroyEvent;
    /// <inheritdoc/>
    public Activity Activity => this;

    /// <summary>
    /// Creates the activity's options: the access-key schemes and file types a head takes. The default takes none.
    /// </summary>
    /// <returns>The options.</returns>
    protected virtual AndroidMainActivityOptions CreateActivityOptions()
    {
        return new AndroidMainActivityOptions();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Before the view is made, it prepares the UI's content, waiting on a version's first run while the package's zip is
    /// extracted, gives the app its UI context, and requests portrait on a phone; then it raises
    /// <see cref="CreateEvent"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The app's options name no UI content.</exception>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Before base.OnCreate, which makes the view (the UI hands this activity its
        // factory): the second half of the UI's start, whose first the Application ran
        // (AndroidAvaloniaApplication). The UI's store is, on Android, the zip's extraction out of
        // the package (ZipAssetProvider), made on the first run of a version - waited for here,
        // deliberately, as every frame after this line depends on it. Preparing the content also
        // registers the UI's fonts, Avalonia having started with the process's Application, before
        // the store could be read. Then the app is told which languages the UI has; in process that
        // completes at once.
        AvaloniaUiHosting.PrepareAsync<TUi>(VpnHoodApp.Instance.UiAssetProvider, CancellationToken.None)
            .GetAwaiter().GetResult();

        // also before base.OnCreate, so the handler is subscribed when the create event fires; the system
        // bars take the UI's chrome colour, which its theme's manifest gave as the UI was prepared
        MainActivityHandler = new AndroidAvaloniaMainActivityHandler(this, CreateActivityOptions(),
            Task.FromResult((AvaloniaApplication.Current as VpnHoodAvaloniaAppBase)?.ChromeColor));

        // and the app's UI context before the view, which reads the account as it is built: a read with
        // no window skips zero-tap sign-in restoration, and on a restored phone the person would have
        // to sign in again by hand
        MainActivityHandler.TakeUiContext();

        ApplyOrientation();
        base.OnCreate(savedInstanceState);
        CreateEvent?.Invoke(this, new CreateEventArgs { SavedInstanceState = savedInstanceState });
    }

    // A phone stays upright: the UI's wide layout is for tablets, TVs and windows, not a phone on its
    // side. Asked at run time, never in the manifest, whose portrait would make Play hide the app from
    // TVs. Asked again on every configuration change, so a fold opened into a tablet turns.
    private void ApplyOrientation()
    {
        RequestedOrientation = AndroidUtils.IsPhone(this)
            ? ScreenOrientation.Portrait
            : ScreenOrientation.Unspecified;
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="NewIntentEvent"/>.</remarks>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        NewIntentEvent?.Invoke(this, new NewIntentEventArgs { Intent = intent });
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="RequestPermissionsResultEvent"/>.</remarks>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        RequestPermissionsResultEvent?.Invoke(this, new RequestPermissionsResultArgs {
            RequestCode = requestCode,
            Permissions = permissions,
            GrantResults = grantResults
        });
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="KeyDownEvent"/> first; a key a handler handles does not reach the base.</remarks>
    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        var args = new KeyDownArgs { KeyCode = keyCode, KeyEvent = e };
        KeyDownEvent?.Invoke(this, args);
        return args.IsHandled || base.OnKeyDown(keyCode, e);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ActivityResultEvent"/>.</remarks>
    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        ActivityResultEvent?.Invoke(this, new ActivityResultEventArgs {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });
        base.OnActivityResult(requestCode, resultCode, data);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ResumeEvent"/>, and tells the app's UI context the app is in front again.</remarks>
    protected override void OnResume()
    {
        base.OnResume();
        ResumeEvent?.Invoke(this, EventArgs.Empty);
        AppUiContext.NotifyResumed();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="PauseEvent"/>.</remarks>
    protected override void OnPause()
    {
        PauseEvent?.Invoke(this, EventArgs.Empty);
        base.OnPause();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="DestroyEvent"/>, and lets the app's main activity handler go.</remarks>
    protected override void OnDestroy()
    {
        DestroyEvent?.Invoke(this, EventArgs.Empty);
        MainActivityHandler = null;
        base.OnDestroy();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It requests portrait again on a phone, and releases the request on a larger screen, as a fold opened into a tablet;
    /// then it raises <see cref="ConfigurationChangedEvent"/>.
    /// </remarks>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplyOrientation();
        ConfigurationChangedEvent?.Invoke(this, newConfig);
    }
}
