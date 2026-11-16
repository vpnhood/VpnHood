using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Runtime;
using Android.Views;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Represents an Android activity that raises an event for each of its lifecycle calls and results, so the app's parts
/// can follow it without deriving from it.
/// </summary>
public class ActivityEvent : Activity, IActivityEvent
{
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

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="CreateEvent"/> once the activity is created.</remarks>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CreateEvent?.Invoke(this, new CreateEventArgs {
            SavedInstanceState = savedInstanceState
        });
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="NewIntentEvent"/>, and does not call the base.</remarks>
    protected override void OnNewIntent(Intent? intent)
    {
        NewIntentEvent?.Invoke(this, new NewIntentEventArgs {
            Intent = intent
        });
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="RequestPermissionsResultEvent"/>, then calls the base.</remarks>
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
    /// <remarks>
    /// It raises <see cref="KeyDownEvent"/>; a handler that sets <see cref="KeyDownArgs.IsHandled"/> keeps the key from
    /// the base, and the result is then <see langword="true"/>.
    /// </remarks>
    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        var args = new KeyDownArgs {
            KeyCode = keyCode,
            KeyEvent = e
        };

        KeyDownEvent?.Invoke(this, args);
        return args.IsHandled ? args.IsHandled : base.OnKeyDown(keyCode, e);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ActivityResultEvent"/>, then calls the base.</remarks>
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
    /// <remarks>It raises <see cref="ResumeEvent"/> after the base.</remarks>
    protected override void OnResume()
    {
        base.OnResume();
        ResumeEvent?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="PauseEvent"/> before the base.</remarks>
    protected override void OnPause()
    {
        PauseEvent?.Invoke(this, EventArgs.Empty);
        base.OnPause();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="DestroyEvent"/> before the base.</remarks>
    protected override void OnDestroy()
    {
        DestroyEvent?.Invoke(this, EventArgs.Empty);
        base.OnDestroy();
    }

    /// <inheritdoc/>
    /// <remarks>It raises <see cref="ConfigurationChangedEvent"/> after the base.</remarks>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ConfigurationChangedEvent?.Invoke(this, newConfig);
    }
}