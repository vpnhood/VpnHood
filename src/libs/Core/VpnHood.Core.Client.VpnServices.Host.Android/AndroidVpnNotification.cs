using Android.Content;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host.Android;

internal sealed class AndroidVpnNotification : IDisposable
{
    private string _sessionName;
    private VpnServiceLocalization _localization;
    private const string DisconnectAction = "disconnect";
    private const string ChannelGeneralId = "general";
    private const string ChannelGeneralName = "General";
    private Notification.Builder _notificationBuilder;
    private readonly Lock _stateLock = new();
    private ClientState? _lastClientState;
    public const int NotificationId = 3500;

    public AndroidVpnNotification(
        Context context,
        VpnServiceLocalization serviceLocalization,
        string sessionName)
    {
        _sessionName = sessionName;
        _localization = serviceLocalization;
        _notificationBuilder = _notificationBuilder = CreateNotificationBuilder(context, serviceLocalization);
        Update(ClientState.None);
    }

    public Notification Build()
    {
        lock (_stateLock)
            return _notificationBuilder.Build();
    }

    // The localization and the session's name can come once the notification is up (AndroidVpnService),
    // and the buttons carry the localization too: a new builder. A state the host has given is shown again
    // at once; before one, the promotion's notification stays until the host's first state.
    public void SetLocalization(Context context, VpnServiceLocalization serviceLocalization, string sessionName)
    {
        lock (_stateLock) {
            var notificationBuilder = CreateNotificationBuilder(context, serviceLocalization);
            _notificationBuilder.Dispose();
            _notificationBuilder = notificationBuilder;
            _localization = serviceLocalization;
            _sessionName = sessionName;

            if (_lastClientState is { } clientState && clientState != ClientState.None)
                Show(clientState);
            else
                _lastClientState = null;
        }
    }

    private static Notification.Builder CreateNotificationBuilder(Context context,
        VpnServiceLocalization serviceLocalization)
    {
        Notification.Builder notificationBuilder;

        // check notification manager
        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        ArgumentNullException.ThrowIfNull(notificationManager);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.PackageManager);

        //create channel
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) {
            var channel = new NotificationChannel(ChannelGeneralId, ChannelGeneralName, NotificationImportance.Low);
            channel.EnableVibration(false);
            channel.EnableLights(false);
            channel.SetShowBadge(false);
            channel.LockscreenVisibility = NotificationVisibility.Public;
            notificationManager.CreateNotificationChannel(channel);
            notificationBuilder = new Notification.Builder(context, ChannelGeneralId);
        }
        else {
            notificationBuilder = new Notification.Builder(context);
        }

        // for android 5.1 (no subtext will be shown if we don't call SetContentText)
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            notificationBuilder.SetContentText(AndroidUtils.GetAppName(context));

        // default intent (manage)
        const PendingIntentFlags intentFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var manageIntent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
        var pendingIntent = PendingIntent.GetActivity(context, 1, manageIntent, intentFlags);
        notificationBuilder.SetContentIntent(pendingIntent);
        notificationBuilder.AddAction(new Notification.Action.Builder(null, serviceLocalization.Manage, pendingIntent)
            .Build());

        //  disconnect intent
        var disconnectIntent = new Intent(context, typeof(AndroidVpnService));
        disconnectIntent.SetAction(DisconnectAction);
        disconnectIntent.PutExtra("manual", true);
        pendingIntent = PendingIntent.GetService(context, 2, disconnectIntent, intentFlags);
        notificationBuilder.AddAction(
            new Notification.Action.Builder(null, serviceLocalization.Disconnect, pendingIntent).Build());

        notificationBuilder.SetOngoing(true); // ignored by StartForeground
        notificationBuilder.SetAutoCancel(false); // ignored by StartForeground
        notificationBuilder.SetVisibility(NotificationVisibility.Secret); //VPN icon is already showed by the system
        var windowBackgroundColor =
            VpnServiceLocalization.TryParseColorFromHex(serviceLocalization.WindowBackgroundColor ?? string.Empty);
        if (windowBackgroundColor != null)
            notificationBuilder.SetColor(windowBackgroundColor.Value.ToAndroidColor());

        // set the required small icon
        ArgumentNullException.ThrowIfNull(context.ApplicationInfo);
        ArgumentNullException.ThrowIfNull(context.Resources);
        var iconId = context.Resources.GetIdentifier("notification", "drawable", context.PackageName);
        if (iconId == 0) iconId = context.ApplicationInfo.Icon;
        notificationBuilder.SetSmallIcon(iconId);

        return notificationBuilder;
    }

    public void Update(ClientState clientState)
    {
        lock (_stateLock) {
            // update only when the state changed
            if (_lastClientState == clientState)
                return;

            Show(clientState);
        }
    }

    // under _stateLock
    private void Show(ClientState clientState)
    {
        // connection status
        // Set subtitle
        var sessionName = _sessionName;
        _notificationBuilder.SetContentTitle(sessionName);
        _notificationBuilder.SetSubText(StateText(clientState));

        // progress
        if (clientState != ClientState.Connected)
            _notificationBuilder.SetProgress(100, 0, true);
        else
            _notificationBuilder.SetProgress(0, 0, false);


        // show or hide
        var notificationManager =
            (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
        if (notificationManager == null)
            return;

        if (clientState != ClientState.None)
            notificationManager.Notify(NotificationId, _notificationBuilder.Build());
        else
            notificationManager.Cancel(NotificationId);

        // set it at the end of method to make sure change is applied without any exception
        _lastClientState = clientState;
    }

    // The connection's state as the app's main page says it; a step on the way ends in "…". The
    // notification comes up as the service starts, before any state: that is initializing.
    private string StateText(ClientState clientState)
    {
        var state = clientState == ClientState.None ? ClientState.Initializing : clientState;
        var text = _localization.GetClientStateText(state);
        return state is ClientState.Connected or ClientState.Unstable or ClientState.Disposed
            ? text
            : $"{text}…";
    }

    public void Dispose()
    {
        _notificationBuilder.Dispose();
    }
}