using System;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Tunneling.Messages;
using VpnHood.Client.Device;

namespace VpnHood.Client
{
    public class VpnHoodConnect : IDisposable
    {
        private readonly bool _leavePacketCaptureOpen;
        private readonly IPacketCapture _packetCapture;
        private readonly Guid _clientId;
        private readonly Token _token;
        private DateTime _reconnectTime = DateTime.MinValue;
        private readonly ClientOptions _clientOptions;
      
        public event EventHandler ClientStateChanged;
        public int AttemptCount { get; private set; }
        public int ReconnectDelay { get; set; }
        public int MaxReconnectCount { get; set; }
        public VpnHoodClient Client { get; private set; }
        public void Disconnect() => Client?.Dispose();
        public void Dispose() => Client?.Dispose();

        public VpnHoodConnect(IPacketCapture packetCapture, Guid clientId, Token token, ClientOptions clientOptions = null, ConnectOptions connectOptions = null)
        {
            if (connectOptions == null) connectOptions = new ConnectOptions();

            _leavePacketCaptureOpen = clientOptions.LeavePacketCaptureOpen;
            _packetCapture = packetCapture;
            _clientId = clientId;
            _token = token;
            _clientOptions = clientOptions ?? new ClientOptions();
            MaxReconnectCount = connectOptions.MaxReconnectCount;
            ReconnectDelay = connectOptions.ReconnectDelay;

            //this class Connect change this option temporary and restore it after last attempt
            _clientOptions.LeavePacketCaptureOpen = true;
            _clientOptions.UseUdpChannel = connectOptions.UdpChannelMode == UdpChannelMode.On || connectOptions.UdpChannelMode == UdpChannelMode.Auto;

            // let always have a Client
            Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);
        }

        public Task Connect()
        {
            Client = new VpnHoodClient(_packetCapture, _clientId, _token, _clientOptions);
            Client.StateChanged += Client_StateChanged;
            return Client.Connect();
        }
        
        private void Client_StateChanged(object sender, EventArgs e)
        {
            ClientStateChanged?.Invoke(sender, e);
            if (Client.State == ClientState.Disposed)
            {
                var _ = Reconnect();
            }
        }

        private async Task Reconnect()
        {
            if (Client.State != ClientState.Disposed)
                throw new InvalidOperationException("Client has not been disposed yet!");

            if ((DateTime.Now - _reconnectTime).TotalMinutes > 5)
                AttemptCount = 0;

            // check reconnecting
            var resposeCode = Client.SessionStatus.ResponseCode;
            var reconnect = (AttemptCount < MaxReconnectCount &&
                (resposeCode == ResponseCode.GeneralError || resposeCode == ResponseCode.SessionClosed || resposeCode == ResponseCode.InvalidSessionId));
            
            if (reconnect)
            {
                _reconnectTime = DateTime.Now;
                AttemptCount++;
                await Task.Delay(ReconnectDelay);
                await Connect();
            }
            else
            {
                if (!_leavePacketCaptureOpen)
                    _packetCapture.Dispose();
            }
        }
    }
}
