using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.DatagramMessaging;

namespace VpnHood.Core.Tunneling.Channels;

public abstract class PacketChannel : PacketTransport, IJob, IPacketChannel
{
    private readonly TimeSpan? _lifespan;
    private DateTime? _closeRequestTime;
    private bool _started;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    protected CancellationToken CancellationToken => _cancellationTokenSource.Token;
    public string ChannelId { get; }
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);
    public JobSection JobSection { get; } = new();
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
        ChannelId = options.ChannelId;
        JobRunner.Default.Add(this);
    }

    public PacketChannelState State {
        get {
            if (IsDisposed) return
                PacketChannelState.Disposed;

            if (_closeRequestTime != null)
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
        _cancellationTokenSource.Cancel();
    }

    private async Task StartTask()
    {
        try {
            await StartReadTask();
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
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel read task completed. ChannelId: {ChannelId}, Type: {Type}",
                ChannelId, VhLogger.FormatType(this));
            Dispose();
        }
    }

    private void CheckLifetime()
    {
        // if channel is disposed, do nothing
        if (IsDisposed || _cancellationTokenSource.IsCancellationRequested)
            return;

        // dispose if its lifetime is over
        if (_started && _closeRequestTime == null &&
            _lifespan.HasValue && FastDateTime.Now - PacketStat.CreatedTime > _lifespan.Value) {

            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel lifetime is over. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
                ChannelId, PacketStat.CreatedTime, _lifespan);

            // send close message if not already sent
            SendPacketQueued(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));
            _closeRequestTime = FastDateTime.Now; // mark as closing
        }

        // dispose if _closeMessageTime is set and more than graceful timeout
        if (FastDateTime.Now - _closeRequestTime > TunnelDefaults.TcpGracefulTimeout)
            Dispose();
    }


    protected override void OnPacketReceived(IpPacket ipPacket)
    {
        //todo need test
        // check close message
        var message = PacketMessageHandler.ReadMessage(ipPacket);
        if (message is ClosePacketMessage) {
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel received close message. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
                ChannelId, PacketStat.CreatedTime, _lifespan);

            _closeRequestTime ??= FastDateTime.Now;
            ipPacket.Dispose();
            return;
        }

        base.OnPacketReceived(ipPacket);
    }

    public Task RunJob()
    {
        CheckLifetime();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            JobRunner.Default.Remove(this);
            Stop();
        }

        base.Dispose(disposing);
    }
}