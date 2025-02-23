using Android;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Service.QuickSettings;
using Java.Lang;
using Java.Util.Functions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Common.Logging;
using Exception = System.Exception;
using Object = Java.Lang.Object;

namespace VpnHood.AppLib.Droid.Common;

[Service(Permission = Manifest.Permission.BindQuickSettingsTile, Icon = IconResourceName, Enabled = true,
    Exported = true)]
[MetaData(MetaDataToggleableTile, Value = "true")]
[MetaData(MetaDataActiveTile, Value = "true")]
[IntentFilter([ActionQsTile])]
public class QuickLaunchTileService : TileService
{
    private const string IconResourceName = "@mipmap/quick_launch_tile";
    private bool _isConnectByClick;

    public override void OnCreate()
    {
        base.OnCreate();

        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged!;
        Refresh();
    }

    private void ConnectionStateChanged(object sender, EventArgs e)
    {
        Refresh();

        // toast last error
        if (_isConnectByClick && VpnHoodApp.Instance.ConnectionState == AppConnectionState.None) {
            _isConnectByClick = false;
            if (VpnHoodApp.Instance.State.LastError != null)
                Toast.MakeText(this, VpnHoodApp.Instance.State.LastError?.Message, ToastLength.Long)?.Show();
        }
    }

    public override void OnClick()
    {
        try {
            if (VpnHoodApp.Instance.ConnectionState == AppConnectionState.None) {
                _isConnectByClick = true;
                _ = VpnHoodApp.Instance.Connect();
            }
            else {
                _ = VpnHoodApp.Instance.Disconnect();
            }
        }
        catch (Exception ex) {
            Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show();
        }

        Refresh();
    }

    public override void OnTileAdded()
    {
        VpnHoodApp.Instance.Settings.IsQuickLaunchEnabled = true;
        VpnHoodApp.Instance.Settings.Save();
        base.OnTileAdded();
        Refresh();
    }

    public override void OnTileRemoved()
    {
        VpnHoodApp.Instance.Settings.IsQuickLaunchEnabled = false;
        VpnHoodApp.Instance.Settings.Save();
        base.OnTileRemoved();
    }


    public override void OnStartListening()
    {
        base.OnStartListening();
        if (VpnHoodApp.Instance.Settings.IsQuickLaunchEnabled == false) {
            VpnHoodApp.Instance.Settings.IsQuickLaunchEnabled = true;
            VpnHoodApp.Instance.Settings.Save();
        }

        Refresh();
    }

    private void Refresh()
    {
        if (QsTile == null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            QsTile.StateDescription = VpnHoodApp.Instance.ConnectionState.ToString();

        var currentProfileInfo = VpnHoodApp.Instance.CurrentClientProfileInfo;
        if (currentProfileInfo != null && !VpnHoodApp.Instance.IsIdle) {
            QsTile.Label = currentProfileInfo.ClientProfileName;
            QsTile.State = VpnHoodApp.Instance.ConnectionState ==
                           AppConnectionState.Connected
                ? TileState.Active
                : TileState.Unavailable;
        }
        else if (currentProfileInfo != null) {
            QsTile.Label = currentProfileInfo.ClientProfileName;
            QsTile.State = TileState.Inactive;
        }
        else {
            QsTile.Label = AndroidUtil.GetAppName(Application.Context);
            QsTile.State = TileState.Unavailable;
        }

        QsTile.UpdateTile();
    }

    private class AddTileServiceHandler(TaskCompletionSource<int> taskCompletionSource)
        : Object, IConsumer
    {
        public void Accept(Object? obj)
        {
            obj ??= 0;
            taskCompletionSource.TrySetResult((int)obj);
        }
    }

    public static Task<int> RequestAddTile(Context context)
    {
        var taskCompletionSource = new TaskCompletionSource<int>();

        // get statusBarManager
        if (context.GetSystemService(StatusBarService) is not StatusBarManager statusBarManager) {
            VhLogger.Instance.LogError("Could not retrieve the StatusBarManager.");
            return Task.FromResult(0);
        }

        if (context.MainExecutor == null) {
            VhLogger.Instance.LogError("Could not retrieve the MainExecutor.");
            return Task.FromResult(0);
        }

        ArgumentNullException.ThrowIfNull(context.PackageManager);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.Resources);
        var appName =
            context.PackageManager.GetApplicationLabel(
                context.PackageManager.GetApplicationInfo(context.PackageName, PackageInfoFlags.MetaData));
        var iconId = context.Resources.GetIdentifier(IconResourceName, "drawable", context.PackageName);
        var icon = Icon.CreateWithResource(context, iconId);

        statusBarManager.RequestAddTileService(
            new ComponentName(context, Class.FromType(typeof(QuickLaunchTileService))),
            appName, icon,
            context.MainExecutor!,
            new AddTileServiceHandler(taskCompletionSource));

        return taskCompletionSource.Task;
    }
}