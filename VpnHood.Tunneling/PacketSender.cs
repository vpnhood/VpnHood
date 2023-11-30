using System.Net;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Tunneling;

public class PacketSender
{
    private readonly List<IPPacket> _packets = new();
    private readonly SessionProxyManager _sessionProxyManager;
    private readonly AsyncLock _sendLock = new();
    private readonly SemaphoreSlim _packetArrivalSignal = new(1, 1);

    public PacketSender(ISocketFactory socketFactory, ProxyManagerOptions proxyManagerOptions)
    {
        _sessionProxyManager = new SessionProxyManager(this, socketFactory, proxyManagerOptions);
    }

    public async Task<IPPacket[]> Send(IPPacket[] ipPackets, TimeSpan timeSpan)
    {
        await _sessionProxyManager.SendPackets(ipPackets);
        using var cts = new CancellationTokenSource(timeSpan);
        using var sendLock = await _sendLock.LockAsync(cts.Token);

        // ReSharper disable once InconsistentlySynchronizedField
        while (_packets.Count < ipPackets.Length && !cts.Token.IsCancellationRequested)
            await _packetArrivalSignal.WaitAsync(cts.Token);

        lock (_packets)
        {
            var results = _packets.ToArray();
            _packets.Clear();
            return results;
        }
    }

    private class SessionProxyManager : ProxyManager
    {
        private readonly PacketSender _packetSender;
        protected override bool IsPingSupported => true;

        public SessionProxyManager(PacketSender packetSender, ISocketFactory socketFactory, ProxyManagerOptions options)
            : base(socketFactory, options)
        {
            _packetSender = packetSender;
        }

        public override Task OnPacketReceived(IPPacket ipPacket)
        {
            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

            //ipPacket = _packetSender._netFilter.ProcessReply(ipPacket);
            lock (_packetSender._packets)
                _packetSender._packets.Add(ipPacket);

            _packetSender._packetArrivalSignal.Release();
            return Task.CompletedTask;
        }

        public override void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            //_packetSender.LogTrack(protocolType.ToString(), localEndPoint, remoteEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint, null);
        }

        public override void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
        {
            //_packetSender.VerifyNetScan(protocolType, remoteEndPoint, "OnNewRemoteEndPoint");
        }
    }
}