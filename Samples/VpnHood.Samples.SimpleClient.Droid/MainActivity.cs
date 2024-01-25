
using VpnHood.Client.Device.Droid;
using VpnHood.Client.Device.Droid.Utils;
using VpnHood.Common;

// ReSharper disable StringLiteralTypo
namespace VpnHood.Client.App.Droid;

[Activity(Label = "@string/app_name", MainLauncher = true)]
// ReSharper disable once UnusedMember.Global
public class MainActivity : ActivityEvent
{
    private static readonly AndroidDevice Device = new();
    private VpnHoodClient? _vpnHoodClient;
    private Button _connectButton = default!;
    private TextView _statusTextView = default!;

    private bool IsConnectingOrConnected =>
        _vpnHoodClient?.State is ClientState.Connecting or ClientState.Connected;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Device.Prepare(this);

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

    private void ConnectButton_Click(object? sender, EventArgs e)
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
                await Disconnect();
                return;
            }

            // Connect
            // accessKey must obtain from the server
            var clientId = Guid.Parse("7BD6C156-EEA3-43D5-90AF-B118FE47ED0B");
            var accessKey = "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy82YWlrdHFmM2xhZW9vaGY/ZGw9MSIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIl19";
            var token = Token.FromAccessKey(accessKey);
            var packetCapture = await Device.CreatePacketCapture();

            _vpnHoodClient = new VpnHoodClient(packetCapture, clientId, token, new ClientOptions());
            _vpnHoodClient.StateChanged += (_, _) => UpdateUi();
            await _vpnHoodClient.Connect();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async Task Disconnect()
    {
        if (_vpnHoodClient != null)
            await _vpnHoodClient.DisposeAsync();
        _vpnHoodClient = null;
    }

    private void UpdateUi()
    {
        RunOnUiThread(() =>
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
}