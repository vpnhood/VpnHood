using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using VpnHood.Client;
using VpnHood.Client.Device.Android;
using VpnHood.Common;
using Xamarin.Essentials;

// ReSharper disable StringLiteralTypo

namespace VpnHood.Samples.SimpleClient.Droid
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    // ReSharper disable once UnusedMember.Global
    public class MainActivity : Activity
    {
        private const int RequestVpnPermission = 10;
        private static readonly AndroidDevice Device = new AndroidDevice();
        private static VpnHoodClient _vpnHoodClient;
        private Button _connectButton;
        private TextView _statusTextView;

        private bool IsConnectingOrConnected =>
            _vpnHoodClient?.State == ClientState.Connecting || _vpnHoodClient?.State == ClientState.Connected;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);

            // manage VpnPermission
            Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

            // Set our simple view
            var linearLayout = new LinearLayout(this);

            _connectButton = new Button(this);
            _connectButton.Click += ConnectButton_Click;
            linearLayout.AddView(_connectButton);

            _statusTextView = new TextView(this);
            linearLayout.AddView(_statusTextView);
            SetContentView(linearLayout);
            UpdateUi();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            Task.Run(ConnectTask);
        }

        private async Task ConnectTask()
        {
            try
            {
                // disconnect if already connected
                if (IsConnectingOrConnected)
                {
                    Disconnect();
                    return;
                }

                // Connect
                // accessKey must obtain from the server
                var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0B");
                var accessKey = "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy82YWlrdHFmM2xhZW9vaGY/ZGw9MSIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIl19";
                var token = Token.FromAccessKey(accessKey);
                var packetCapture = await Device.CreatePacketCapture();

                _vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());
                _vpnHoodClient.StateChanged += (sender, e) => UpdateUi();
                await _vpnHoodClient.Connect();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void Disconnect()
        {
            _vpnHoodClient?.Dispose();
            _vpnHoodClient = null;
        }


        private void UpdateUi()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_vpnHoodClient == null || _vpnHoodClient.State == ClientState.None ||
                    _vpnHoodClient.State == ClientState.Disposed)
                {
                    _connectButton.Text = "Connect";
                    _statusTextView.Text = "Disconnected";
                }
                else if (_vpnHoodClient.State == ClientState.Connecting)
                {
                    _connectButton.Text = "Disconnect";
                    _statusTextView.Text = "Connecting";
                }
                else if (_vpnHoodClient.State == ClientState.Connected)
                {
                    _connectButton.Text = "Disconnect";
                    _statusTextView.Text = "Connected";
                }
            });
        }

        private void Device_OnRequestVpnPermission(object sender, EventArgs e)
        {
            var intent = VpnService.Prepare(this);
            if (intent == null)
            {
                Device.VpnPermissionGranted();
            }
            else
            {
                StartActivityForResult(intent, RequestVpnPermission);
            }
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            if (requestCode == RequestVpnPermission && resultCode == Result.Ok)
                Device.VpnPermissionGranted();
            else
                Device.VpnPermissionRejected();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        protected override void OnDestroy()
        {
            Device.OnRequestVpnPermission -= Device_OnRequestVpnPermission;
            base.OnDestroy();
        }
    }
}