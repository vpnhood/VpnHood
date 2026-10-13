using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

public abstract class PacketChannel : PacketTransport, IPacketChannel
{
    private readonly TimeSpan? _lifespan;

    // when the channel began closing, by its lifespan or by the peer's close: from then on it is Disconnecting
    private DateTime? _closeTime;
    private bool _started;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Job _checkLifetimeJob;
    protected TrafficMeter? TrafficMeter { get; }

    protected CancellationToken CancellationToken => _cancellationTokenSource.Token;
    protected override string Name { get; }

    public string ChannelId { get; }
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);
    protected abstract Task StartReadTask();
    public abstract int OverheadLength { get; }

    protected PacketChannel(PacketChannelOptions options)
        : base(new PacketTransportOptions {
            AutoDisposePackets = options.AutoDisposePackets,
            Blocking = options.Blocking,
            QueueCapacity = options.QueueCapacity
        })
    {
        _lifespan = options.Lifespan;
        TrafficMeter = options.TrafficMeter;
        Name = $"{VhLogger.FormatType(this)}: {options.ChannelId}";
        ChannelId = options.ChannelId;
        _checkLifetimeJob = new Job(CheckLifetime, nameof(PacketChannel));
    }

    public PacketChannelState State {
        get {
            if (IsDisposed)
                return
                    PacketChannelState.Disposed;

            if (_closeTime != null)
                return PacketChannelState.Disconnecting;

            return _started
                ? PacketChannelState.Connected
                : PacketChannelState.NotStarted;
        }
    }

    public void Start()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_started)
            throw new InvalidOperationException("The packet channel is already started.");

        // start reading packets
        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "Starting a PacketChannel. ChannelId: {ChannelId}", ChannelId);

        _ = StartTask();
        _started = true;
    }


    protected void Stop()
    {
        _cancellationTokenSource.TryCancel();
    }

    private async Task StartTask()
    {
        try {
            await StartReadTask();
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel read task completed. ChannelId: {ChannelId}, Type: {Type}",
                ChannelId, VhLogger.FormatType(this));
        }
        catch (OperationCanceledException) when (IsDisposed || _cancellationTokenSource.IsCancellationRequested) {
            // normal cancellation, do nothing
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.PacketChannel, ex,
                "PacketChannel read task failed. ChannelId: {ChannelId}, Type: {Type}",
                ChannelId, VhLogger.FormatType(this));
        }
        finally {
            Dispose();
        }
    }

    private ValueTask CheckLifetime(CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(CheckLifetime));

        // if channel is disposed, do nothing
        if (_cancellationTokenSource.IsCancellationRequested)
            return default;

        // the lifespan is over: Disconnecting first, so the tunnel stops queueing on the channel, then the close
        if (_started && _closeTime == null &&
            _lifespan.HasValue && FastDateTime.UtcNow - PacketStat.CreatedTime > _lifespan.Value) {
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel lifetime is over. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
                ChannelId, PacketStat.CreatedTime, _lifespan);

            _closeTime = FastDateTime.UtcNow;
            SendPacketQueued(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));
        }

        // a closing channel goes after its grace, in which the packets already on their way arrive
        if (FastDateTime.UtcNow - _closeTime > TransportDefaults.TcpGracefulTimeout)
            Dispose();

        return default;
    }


    protected override void OnPacketReceived(IpPacket ipPacket)
    {
        if (!PacketMessageHandler.IsPacketMessage(ipPacket)) {
            base.OnPacketReceived(ipPacket);
            return;
        }

        // A message is the channel's own, so it is disposed here. One it cannot handle, such as a newer peer's, is
        // dropped as the base drops a packet it cannot deliver, rather than ending the channel
        try {
            if (PacketMessageHandler.ReadMessage(ipPacket) is ClosePacketMessage)
                ReceiveClose();
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Dropping a packet message the channel could not handle.");
        }
        finally {
            ipPacket.Dispose();
        }
    }

    // The peer's close: the channel is Disconnecting and goes after its grace, as the peer's does. It sends no answer:
    // each end reads for its whole grace, so a packet queued as the close arrived still comes through, where an end
    // that closed on an answer would lose one queued behind it
    private void ReceiveClose()
    {
        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "PacketChannel received close message. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
            ChannelId, PacketStat.CreatedTime, _lifespan);

        _closeTime ??= FastDateTime.UtcNow;
    }

    protected override void DisposeManaged()
    {
        Stop();
        _checkLifetimeJob.Dispose();
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        base.DisposeManaged();
    }
}