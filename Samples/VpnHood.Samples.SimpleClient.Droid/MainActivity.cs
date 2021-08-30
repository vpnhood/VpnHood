using Android.App;
using Android.Content;
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

        private async Task ConnectTask()
        {
            try
            {
                // disconnect if already connected
                if (IsConnectingOrConnected)
                    Disconnect();

                // Connect
                // accessKey must obtain from the server
                var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0B");
                var accessKey = "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6MTEsInRpZCI6IjEwNDczNTljLWExMDctNGU0OS04NDI1LWMwMDRjNDFmZmI4ZiIsInNlYyI6IlRmK1BpUTRaS1oyYW1WcXFPNFpzdGc9PSIsImRucyI6Im1vLmdpd293eXZ5Lm5ldCIsImlzdmRucyI6ZmFsc2UsInBraCI6Ik1Da3lsdTg0N2J5U0Q4bEJZWFczZVE9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiXSwicGIiOnRydWUsInVybCI6Imh0dHBzOi8vd3d3LmRyb3Bib3guY29tL3MvaG1oY2g2YjA5eDdmdXgzL3B1YmxpYy5hY2Nlc3NrZXk/ZGw9MSJ9";
                var token = Token.FromAccessKey(accessKey);
                var packetCapture = await Device.CreatePacketCapture();

                VpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());
                VpnHoodClient.StateChanged += (object sender, EventArgs e) => UpdateUI();
                await VpnHoodClient.Connect();
            }
            catch (Exception ex)
            {
                var str = ex.Message;
            }
        }

        private void Disconnect()
        {
            VpnHoodClient?.Dispose();
            VpnHoodClient = null;
        }

        private bool IsConnectingOrConnected =>
            VpnHoodClient?.State == ClientState.Connecting || VpnHoodClient?.State == ClientState.Connected;


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