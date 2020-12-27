using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using System;
using System.Threading.Tasks;
using VpnHood.Client;
using VpnHood.Client.Device.Android;
using VpnHood.Common;
using Xamarin.Essentials;

namespace VpnHood.Samples.SimpleClient.Droid
{

    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private const int REQUEST_VpnPermission = 10;
        private static readonly AndroidDevice Device = new AndroidDevice();
        private static VpnHoodClient VpnHoodClient;
        private Button ConnectButton;
        private TextView StatusTextView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);

            // manage VpnPermission
            Device.OnRequestVpnPermission += Device_OnRequestVpnPermission;

            // Set our simple view
            var linearLayout = new LinearLayout(this);

            ConnectButton = new Button(this);
            ConnectButton.Click += ConnectButton_Click;
            linearLayout.AddView(ConnectButton);

            StatusTextView = new TextView(this);
            linearLayout.AddView(StatusTextView);
            SetContentView(linearLayout);
            UpdateUI();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            Task.Run(ConnectTask);
        }

        private void Disconnect()
        {
            VpnHoodClient?.Dispose();
            VpnHoodClient = null;
        }

        private bool IsConnectingOrConnected => VpnHoodClient?.State == ClientState.Connecting || VpnHoodClient?.State == ClientState.Connected;

        private async Task ConnectTask()
        {
            try
            {
                // disconnect if already connected
                if (IsConnectingOrConnected)
                    Disconnect();

                // Connect
                // accessKey must obtain from the server
                var accessKey = "eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6NCwidGlkIjoiMmMwMmFjNDEtMDQwZi00NTc2LWI4Y2MtZGNmZTViOTE3MGI3Iiwic2VjIjoid3hWeVZvbjkxME9iYURDNW9BenpCUT09IiwiZG5zIjoiYXp0cm8uc2lnbWFsaWIub3JnIiwiaXN2ZG5zIjpmYWxzZSwicGtoIjoiUjBiaEsyNyt4dEtBeHBzaGFKbGk4dz09IiwiZXAiOlsiNTEuODEuODQuMTQyOjQ0MyJdLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy9obWhjaDZiMDl4N2Z1eDMvcHVibGljLmFjY2Vzc2tleT9kbD0xIn0=";
                var token = Token.FromAccessKey(accessKey);
                var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0B");
                var packetCapture = await Device.CreatePacketCapture();

                VpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());
                VpnHoodClient.OnStateChanged += (object sender, EventArgs e) => UpdateUI();
                await VpnHoodClient.Connect();
            }
            catch (Exception ex)
            {
                var str = ex.Message;
            }
        }

        private void UpdateUI()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (VpnHoodClient == null || VpnHoodClient.State == ClientState.None || VpnHoodClient.State == ClientState.Disposed)
                {
                    ConnectButton.Text = "Connect";
                    StatusTextView.Text = "Disconnected";
                }
                else if (VpnHoodClient.State == ClientState.Connecting)
                {
                    ConnectButton.Text = "Disconnect";
                    StatusTextView.Text = "Connecting";
                }
                else if (VpnHoodClient.State == ClientState.Connected)
                {
                    ConnectButton.Text = "Disconnect";
                    StatusTextView.Text = "Connected";
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
                StartActivityForResult(intent, REQUEST_VpnPermission);
            }
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            if (requestCode == REQUEST_VpnPermission && resultCode == Result.Ok)
                Device.VpnPermissionGranted();
            else
                Device.VpnPermissionRejected();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
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