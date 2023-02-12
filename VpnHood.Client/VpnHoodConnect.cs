using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;

namespace VpnHood.Client;

public class VpnHoodConnect : IAsyncDisposable
{
    private readonly bool _autoDisposePacketCapture;
    private readonly Guid _clientId;
    private readonly ClientOptions _clientOptions;
    private readonly IPacketCapture _packetCapture;
    private readonly Token _token;
    private DateTime _reconnectTime = DateTime.MinValue;

    public bool IsWaiting { get; private set; }
    public bool IsDisposed { get; private set; }
    public event EventHandler? StateChanged;

    public VpnHoodConnect(IPacketCapture packetCapture, Guid clientId, Token token,
        ClientOptions? clientOptions = null, ConnectOptions? connectOptions = null)
    {
        connectOptions ??= new ConnectOptions();
        _clientOptions = clientOptions ?? new ClientOptions();
        _autoDisposePacketCapture = _clientOptions.AutoDisposePacketCapture;
        _packetCapture = packetCapture;
        _clientId = clientId;
        _token = token;
        MaxReconnectCount = connectOptions.MaxReconnectCount;
        ReconnectDelay = connectOptions.ReconnectDelay;

        //this class Connect change this option temporary and restore it after last attempt
        _clientOptions.AutoDisposePacketCapture = false;
        _clientOptions.UseUdpChannel = connectOptions.UdpChannelMode == UdpChannelMode.On ||
                                       connectOptions.UdpChannelMode == UdpChannelMode.Auto;

        // let always have a Client to access its member after creating VpnHoodConnect
        Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);
    }

    public int AttemptCount { get; private set; }
    public TimeSpan ReconnectDelay { get; set; }
    public int MaxReconnectCount { get; set; }
    public VpnHoodClient Client { get; private set; }

    public Task Connect()
    {
        if (IsDisposed)
            throw new ObjectDisposedException($"{VhLogger.FormatType(this)} is disposed!");

        if (Client.State != ClientState.None && Client.State != ClientState.Disposed)
            throw new InvalidOperationException("Connection is already in progress!");

        if (Client.State == ClientState.Disposed)
            Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);

        Client.StateChanged += Client_StateChanged;
        return Client.Connect();
    }

    private void Client_StateChanged(object sender, EventArgs e)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (Client.State == ClientState.Disposed) _ = Reconnect();
    }

    private async Task Reconnect()
    {
        if ((FastDateTime.Now - _reconnectTime).TotalMinutes > 5)
            AttemptCount = 0;

        // check reconnecting
        var reconnect = AttemptCount < MaxReconnectCount &&
                        Client.SessionStatus.ErrorCode is SessionErrorCode.GeneralError;

        if (reconnect)
        {
            _reconnectTime = FastDateTime.Now;
            AttemptCount++;

            // delay
            IsWaiting = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            await Task.Delay(ReconnectDelay);
            IsWaiting = false;
            StateChanged?.Invoke(this, EventArgs.Empty);

            // connect again
            if (IsDisposed) return;
            await Connect();
        }
        else
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        // close client
        try
        {
            await Client.DisposeAsync();
            Client.StateChanged -= Client_StateChanged; //must be after Client.Dispose to capture dispose event
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError($"Could not dispose client properly! Error: {ex}");
        }

        // release _packetCapture
        if (_autoDisposePacketCapture)
            _packetCapture.Dispose();

        // notify state changed
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

}