using System;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Client.Device;
using VpnHood.Common.Messaging;

namespace VpnHood.Client
{
    public class VpnHoodConnect : IDisposable
    {
        private readonly bool _autoDisposePacketCapture;
        private readonly IPacketCapture _packetCapture;
        private readonly Guid _clientId;
        private readonly Token _token;
        private DateTime _reconnectTime = DateTime.MinValue;
        private readonly ClientOptions _clientOptions;

        public event EventHandler? ClientStateChanged;
        public int AttemptCount { get; private set; }
        public int ReconnectDelay { get; set; }
        public int MaxReconnectCount { get; set; }
        public VpnHoodClient Client { get; private set; }

        public VpnHoodConnect(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions? clientOptions = null, ConnectOptions? connectOptions = null)
        {
            if (connectOptions == null) connectOptions = new ConnectOptions();
            _clientOptions = clientOptions ?? new ClientOptions();
            _autoDisposePacketCapture = _clientOptions.AutoDisposePacketCapture;
            _packetCapture = packetCapture;
            _clientId = clientId;
            _token = token;
            MaxReconnectCount = connectOptions.MaxReconnectCount;
            ReconnectDelay = connectOptions.ReconnectDelay;

            //this class Connect change this option temporary and restore it after last attempt
            _clientOptions.AutoDisposePacketCapture = false;
            _clientOptions.UseUdpChannel = connectOptions.UdpChannelMode == UdpChannelMode.On || connectOptions.UdpChannelMode == UdpChannelMode.Auto;

            // let always have a Client to access its member after creating VpnHoodConnect
            Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);
        }

        public Task Connect()
        {
            if (Client.State != ClientState.None && Client.State != ClientState.Disposed)
                throw new InvalidOperationException("Connection is already in progress!");

            if (Client.State == ClientState.Disposed)
                Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);

            Client.StateChanged += Client_StateChanged;
            return Client.Connect();
        }

        private void Client_StateChanged(object sender, EventArgs e)
        {
            ClientStateChanged?.Invoke(sender, e);
            if (Client.State == ClientState.Disposed)
            {
                _ = Reconnect();
            }
        }

        private async Task Reconnect()
        {
            if (Client.State != ClientState.Disposed)
                throw new InvalidOperationException("Client has not been disposed yet!");

            if ((DateTime.Now - _reconnectTime).TotalMinutes > 5)
                AttemptCount = 0;

            // check reconnecting
            var reconnect = AttemptCount < MaxReconnectCount && Client.ReceivedByteCount > 0 &&
                (Client.SessionStatus.ErrorCode is SessionErrorCode.GeneralError or SessionErrorCode.SessionClosed );

            if (reconnect)
            {
                _reconnectTime = DateTime.Now;
                AttemptCount++;
                await Task.Delay(ReconnectDelay);
                await Connect();
            }
        }

        private bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                Client.StateChanged -= Client_StateChanged;
                Client.Dispose();
                if (_autoDisposePacketCapture)
                    _packetCapture.Dispose();
                _disposed = true;
            }
        }
    }
}
