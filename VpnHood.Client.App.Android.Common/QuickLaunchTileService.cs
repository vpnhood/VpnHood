using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Service.QuickSettings;
using Android.Widget;
using Java.Util.Functions;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Droid.Common;

[Service(Permission = Manifest.Permission.BindQuickSettingsTile, Icon = "@mipmap/quick_launch_tile", Enabled = true, Exported = true)]
[MetaData(MetaDataToggleableTile, Value = "true")]
[MetaData(MetaDataActiveTile, Value = "true")]
[IntentFilter(new[] { ActionQsTile })]
public class QuickLaunchTileService : TileService
{
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
        if (_isConnectByClick && VpnHoodApp.Instance.ConnectionState == AppConnectionState.None)
        {
            _isConnectByClick = false;
            if (!string.IsNullOrEmpty(VpnHoodApp.Instance.State.LastError))
                Toast.MakeText(this, VpnHoodApp.Instance.State.LastError, ToastLength.Long)?.Show();
        }
    }

    public override void OnClick()
    {
        try
        {
            if (VpnHoodApp.Instance.ConnectionState == AppConnectionState.None)
            {
                _isConnectByClick = true;
                _ = VpnHoodApp.Instance.Connect();
            }
            else
            {
                _ = VpnHoodApp.Instance.Disconnect(true);
            }
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show();
        }

        Refresh();
    }

    public override void OnTileAdded()
    {
        VpnHoodApp.Instance.Settings.IsQuickLaunchAdded = true;
        VpnHoodApp.Instance.Settings.Save();
        base.OnTileAdded();
        Refresh();
    }

    public override void OnTileRemoved()
    {
        VpnHoodApp.Instance.Settings.IsQuickLaunchAdded = false;
        VpnHoodApp.Instance.Settings.Save();
        base.OnTileRemoved();
    }


    public override void OnStartListening()
    {
        base.OnStartListening();
        if (VpnHoodApp.Instance.Settings.IsQuickLaunchAdded == false)
        {
            VpnHoodApp.Instance.Settings.IsQuickLaunchAdded = true;
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

        var activeProfileName = VpnHoodApp.Instance.GetActiveClientProfile()?.Name;
        var defaultProfileName = VpnHoodApp.Instance.GetDefaultClientProfile()?.Name;

        if (!string.IsNullOrEmpty(activeProfileName))
        {
            QsTile.Label = activeProfileName;
            QsTile.State = VpnHoodApp.Instance.ConnectionState == 
                AppConnectionState.Connected ? TileState.Active : TileState.Unavailable;
        }
        else if (!string.IsNullOrEmpty(defaultProfileName))
        {
            QsTile.Label = defaultProfileName;
            QsTile.State = TileState.Inactive;
        }
        else
        {
            QsTile.Label = BaseContext?.ApplicationInfo?.Name ?? VpnHoodApp.Instance.Resources.Strings.AppName; //todo check
            QsTile.State = TileState.Unavailable;
        }

        QsTile.UpdateTile();
    }

    private class AddTileServiceHandler : Java.Lang.Object, IConsumer
    {
        private readonly TaskCompletionSource<int> _taskCompletionSource;
        
        public AddTileServiceHandler(TaskCompletionSource<int> taskCompletionSource)
        {
            _taskCompletionSource = taskCompletionSource;
        }

        public void Accept(Java.Lang.Object? obj)
        {
            obj ??= 0;
            _taskCompletionSource.TrySetResult((int)obj);
        }
    }

    public static Task<int> RequestAddTile(Context context)
    {
        var task = new TaskCompletionSource<int>();

        // get statusBarManager
        if (context.GetSystemService(StatusBarService) is not StatusBarManager statusBarManager)
        {
            VhLogger.Instance.LogError("Could not retrieve the StatusBarManager.");
            return Task.FromResult(0);
        }

        if (context.MainExecutor == null)
        {
            VhLogger.Instance.LogError("Could not retrieve the MainExecutor.");
            return Task.FromResult(0);
        }

        ArgumentNullException.ThrowIfNull(context.ApplicationInfo);
        ArgumentNullException.ThrowIfNull(context.PackageManager);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        var appName = context.PackageManager.GetApplicationLabel(context.PackageManager.GetApplicationInfo(context.PackageName, PackageInfoFlags.MetaData));
        var icon = VpnHoodApp.Instance.Resources.Icons.QuickLaunchTileImage?.ToAndroidIcon()
            ?? VpnHoodApp.Instance.Resources.Icons.NotificationImage?.ToAndroidIcon()
            ?? Icon.CreateWithResource(context, context.ApplicationInfo.Icon); 

        statusBarManager.RequestAddTileService(
            new ComponentName(context, Java.Lang.Class.FromType(typeof(QuickLaunchTileService))),
            appName, icon,
            context.MainExecutor!,
            new AddTileServiceHandler(task));
        
        return task.Task;
    }
}