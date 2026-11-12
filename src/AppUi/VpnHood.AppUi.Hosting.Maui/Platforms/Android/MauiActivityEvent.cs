using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Views;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.Maui;

/// <summary>Represents a MAUI activity that raises the app's activity events, for a MAUI UI on Android.</summary>
public class MauiActivityEvent : MauiAppCompatActivity, IActivityEvent
{
    /// <inheritdoc/>
    public event EventHandler<ActivityResultEventArgs>? ActivityResultEvent;
    /// <inheritdoc/>
    public event EventHandler<CreateEventArgs>? CreateEvent;
    /// <inheritdoc/>
    public event EventHandler<NewIntentEventArgs>? NewIntentEvent;
    /// <inheritdoc/>
    /// <remarks>It occurs on a canceled request too, with empty results.</remarks>
    public event EventHandler<RequestPermissionsResultArgs>? RequestPermissionsResultEvent;
    /// <inheritdoc/>
    public event EventHandler<KeyDownArgs>? KeyDownEvent;
    /// <inheritdoc/>
    public event EventHandler? PauseEvent;
    /// <inheritdoc/>
    public event EventHandler? ResumeEvent;
    /// <inheritdoc/>
    public event EventHandler? DestroyEvent;
    /// <inheritdoc/>
    public event EventHandler<Configuration>? ConfigurationChangedEvent;
    /// <inheritdoc/>
    public Activity Activity => this;

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="CreateEvent"/>.</remarks>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CreateEvent?.Invoke(this, new CreateEventArgs
        {
            SavedInstanceState = savedInstanceState
        });
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="NewIntentEvent"/>, and does not call the base.</remarks>
    protected override void OnNewIntent(Intent? intent)
    {
        NewIntentEvent?.Invoke(this, new NewIntentEventArgs
        {
            Intent = intent
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It raises <see cref="RequestPermissionsResultEvent"/>, with empty results where the request was canceled.
    /// </remarks>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        RequestPermissionsResultEvent?.Invoke(this, new RequestPermissionsResultArgs
        {
            RequestCode = requestCode,
            Permissions = permissions,
            GrantResults = grantResults
        });

        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It raises <see cref="KeyDownEvent"/> first; a key the handlers leave handled does not reach the base.
    /// </remarks>
    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        var args = new KeyDownArgs
        {
            KeyCode = keyCode,
            KeyEvent = e
        };

        KeyDownEvent?.Invoke(this, args);
        return args.IsHandled ? args.IsHandled : base.OnKeyDown(keyCode, e);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ActivityResultEvent"/>.</remarks>
    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        ActivityResultEvent?.Invoke(this, new ActivityResultEventArgs
        {
            RequestCode = requestCode,
            ResultCode = resultCode,
            Data = data
        });

        base.OnActivityResult(requestCode, resultCode, data);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ResumeEvent"/>.</remarks>
    protected override void OnResume()
    {
        base.OnResume();
        ResumeEvent?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="PauseEvent"/>.</remarks>
    protected override void OnPause()
    {
        PauseEvent?.Invoke(this, EventArgs.Empty);
        base.OnPause();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="DestroyEvent"/>.</remarks>
    protected override void OnDestroy()
    {
        DestroyEvent?.Invoke(this, EventArgs.Empty);
        base.OnDestroy();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ConfigurationChangedEvent"/>.</remarks>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ConfigurationChangedEvent?.Invoke(this, newConfig);
    }
}