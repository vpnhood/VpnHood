using System;
using Android;
using Android.App;
using Android.Service.QuickSettings;
using Android.Widget;

namespace VpnHood.Client.App.Droid;

[Service(Permission = Manifest.Permission.BindQuickSettingsTile, Icon = "@mipmap/notification", Label = "@string/app_name",
    Enabled = true, Exported = true)]
[MetaData(MetaDataToggleableTile, Value = "true")]
[MetaData(MetaDataActiveTile, Value = "true")]
[IntentFilter(new[] { ActionQsTile })]
public class QuickLaunchTileService : TileService
{
    private bool _isConnectByClick;

    public override void OnCreate()
    {
        base.OnCreate();

        VpnHoodApp.Instance.ConnectionStateChanged += ConnectionStateChanged;
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
        base.OnTileAdded();
        Refresh();
    }

    public override void OnStartListening()
    {
        base.OnStartListening();
        Refresh();
    }

    private void Refresh()
    {
        if (QsTile == null)
            return;

        if (Device.Droid.OperatingSystem.IsAndroidVersionAtLeast(30))
            QsTile.StateDescription = VpnHoodApp.Instance.ConnectionState.ToString();

        QsTile.State = VpnHoodApp.Instance.ConnectionState switch
        {
            AppConnectionState.None => TileState.Inactive,
            AppConnectionState.Connected => TileState.Active,
            _ => TileState.Unavailable
        };

        QsTile.UpdateTile();
    }
}