using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

internal class ChannelManager : IDisposable
{
    private readonly Lock _channelListLock = new();
    private readonly HashSet<IChannel> _disposingChannels = [];
    private readonly HashSet<IProxyChannel> _proxyChannels = [];
    private readonly List<IPacketChannel> _packetChannels = [];
    private readonly EventHandler<IpPacket> _channelPacketReceived;
    private bool _disposed;
    private readonly Job _cleanupJob;

    // a copy taken under the lock, as adds and cleanups change the list under its readers
    public IReadOnlyList<IPacketChannel> GetPacketChannels()
    {
        lock (_channelListLock)
            return _packetChannels.ToArray();
    }

    public ChannelManager(int maxPacketChannelCount, EventHandler<IpPacket> channelPacketReceived)
    {
        MaxPacketChannelCount = maxPacketChannelCount;
        _channelPacketReceived = channelPacketReceived;
        _cleanupJob = new Job(Cleanup, nameof(ChannelManager));
    }

    public int MaxPacketChannelCount {
        get;
        set {
            if (value < 1)
                throw new ArgumentException("Value must equals or greater than 1", nameof(MaxPacketChannelCount));
            field = value;
        }
    }

    public int ProxyChannelCount {
        get {
            lock (_channelListLock)
                return _proxyChannels.Count;
        }
    }

    public int PacketChannelCount {
        get {
            lock (_channelListLock)
                return _packetChannels.Count;
        }
    }

    private void RemoveExcessPacketChannels()
    {
        lock (_channelListLock) {
            while (_packetChannels.Count > MaxPacketChannelCount) {
                RemoveChannel(_packetChannels[0]);
            }
        }
    }

    private bool IsChannelExists(IChannel channel)
    {
        lock (_channelListLock) {
            return channel switch {
                PacketChannel packetChannel => _packetChannels.Contains(packetChannel),
                IProxyChannel proxyChannel => _proxyChannels.Contains(proxyChannel),
                _ => throw new ArgumentOutOfRangeException(nameof(channel))
            };
        }
    }

    // A PacketChannel, not any IPacketChannel: a connection, which the manager closes and cleans up. A tunnel's UDP
    // channel lasts as long as its session and stays beside the manager, so it is refused here
    public void AddChannel(IChannel channel)
    {
        lock (_channelListLock) {
            switch (channel) {
                case PacketChannel packetChannel:
                    AddPacketChannel(packetChannel);
                    break;

                case IProxyChannel proxyChannel:
                    AddProxyChannel(proxyChannel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
    }

    private void AddPacketChannel(IPacketChannel channel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        if (IsChannelExists(channel))
            throw new Exception("the channel already exists in the collection.");

        // start before adding so if exception occurs, it won't be added to the collection
        //should not be called in lock; its behaviour is unexpected
        channel.PacketReceived += _channelPacketReceived;
        channel.Start();

        // add to channel list
        lock (_channelListLock) {
            _packetChannels.Add(channel);

            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "A channel has been added. Channel: {Channel}, ChannelId: {ChannelId}, " +
                "ChannelCount: {ChannelCount}, State: {State}",
                VhLogger.FormatType(channel), channel.ChannelId, _packetChannels.Count, channel.State);
        }

        // The closing and dead channels leave first, so only live ones count against the maximum: a client replacing a
        // closing channel would otherwise cost a full tunnel its oldest healthy one. One that starts closing or dies
        // after the cleanup still counts, as neither takes a lock, and the trim may dispose the oldest instead, healthy
        // or not, with its queued packets. Left so: the gap is microseconds, and a lock would tie every channel, its
        // Dispose included, to a manager it is built before
        CleanupChannels();
        RemoveExcessPacketChannels();
    }

    private void AddProxyChannel(IProxyChannel channel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        if (IsChannelExists(channel))
            throw new Exception("the channel already exists in the collection.");

        channel.Start();

        // should not be called in lock; its behaviour is unexpected
        lock (_channelListLock) {
            _proxyChannels.Add(channel);

            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "A channel has been added. Channel: {Channel}, ChannelId: {ChannelId}, " +
                "ChannelCount: {ChannelCount}, State: {State}",
                VhLogger.FormatType(channel), channel.ChannelId, _proxyChannels.Count, channel.State);
        }
    }

    public void RemoveChannel(IChannel channel, bool dispose = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RemoveChannelInternal(channel, dispose);
    }

    // also the manager's own disposal, which removes every channel once it reads as disposed
    private void RemoveChannelInternal(IChannel channel, bool dispose)
    {
        switch (channel) {
            case PacketChannel packetChannel:
                packetChannel.PacketReceived -= _channelPacketReceived;
                RemoveChannel(_packetChannels, packetChannel, GeneralEventId.PacketChannel, dispose);
                break;

            case IProxyChannel proxyChannel:
                RemoveChannel(_proxyChannels, proxyChannel, GeneralEventId.ProxyChannel, dispose);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private void RemoveChannel<T>(ICollection<T> channels, T channel, EventId eventId, bool dispose) 
        where T : IChannel
    {
        lock (_channelListLock) {
            // remove from both channel and _disposingChannels
            _disposingChannels.Remove(channel);
            channels.Remove(channel);

            // log the removal of the channel
            VhLogger.Instance.LogDebug(eventId,
                "A channel has been removed. Channel: {Channel}, ChannelId: {ChannelId}, " +
                "ChannelCount: {ChannelCount}, State: {State}",
                VhLogger.FormatType(channel), channel.ChannelId, channels.Count, channel.State);
        }

        // clean up channel
        if (dispose)
            channel.Dispose();
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        CleanupChannels();
        return default;
    }

    public void CleanupChannels()
    {
        CleanupChannels(_proxyChannels);
        CleanupChannels(_packetChannels);

        // remove the disposed channels
        lock (_channelListLock) {
            var disposedChannels = _disposingChannels.Where(x => x.State == PacketChannelState.Disposed);
            foreach (var channel in disposedChannels.ToArray()) {
                RemoveChannel(channel);
            }

            RemoveExcessClosingPacketChannels();
        }
    }

    // A peer can close channels faster than their grace ends them, each one holding its connection meanwhile, so at most
    // twice the maximum stay closing: the ones that began closing first go at once. With the live ones that began
    // closing since the last cleanup, a session holds at most three times the maximum, each for 15 to 55 s: its grace,
    // then its lifetime job's next run
    private void RemoveExcessClosingPacketChannels()
    {
        var maxClosingCount = 2 * MaxPacketChannelCount;
        if (_disposingChannels.Count <= maxClosingCount)
            return;

        var closingChannels = _disposingChannels.OfType<PacketChannel>().ToArray();
        var excessChannels = closingChannels
            .OrderBy(x => x.CloseTicks)
            .Take(closingChannels.Length - maxClosingCount);

        foreach (var channel in excessChannels)
            RemoveChannelInternal(channel, dispose: true);
    }

    private void CleanupChannels<T>(ICollection<T> channels) where T : IChannel
    {
        lock (_channelListLock) {
            // prepare the unstable channels for disposal
            var deadChannels = channels.Where(x => x.State != PacketChannelState.Connected);

            // ToArray is required to prevent change in foreach
            foreach (var channel in deadChannels.ToArray()) {
                _disposingChannels.Add(channel);
                channels.Remove(channel);
            }
        }
    }

    public Task RunJob()
    {
        CleanupChannels();
        return Task.CompletedTask;
    }

    // the packet's channel, picked by its source port so a flow keeps to one channel; the count and the pick share
    // one lock
    public IPacketChannel GetPacketChannel(IpPacket ipPacket)
    {
        lock (_channelListLock) {
            var channelCount = _packetChannels.Count;
            var channelIndex = channelCount switch {
                0 => throw new Exception("No available PacketChannel to send packets."),
                1 => 0,
                _ => ipPacket.Protocol switch {
                    IpProtocol.Tcp => ipPacket.ExtractTcp().SourcePort % channelCount,
                    IpProtocol.Udp => ipPacket.ExtractUdp().SourcePort % channelCount,
                    _ => 0
                }
            };

            return _packetChannels[channelIndex];
        }
    }

    public void RemoveChannels<T>(bool dispose = true) where T : IChannel
    {
        // both lists: a proxy channel is never in the packet channels
        IChannel[] channels;
        lock (_channelListLock)
            channels = _packetChannels.Concat<IChannel>(_proxyChannels).Where(x => x is T).ToArray();

        foreach (var channel in channels)
            RemoveChannel(channel, dispose);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // remove all channels and dispose them
        lock (_channelListLock) {
            // first, so an add waiting on the lock, or one a channel's own disposal makes, is refused rather than
            // joining a manager that lets its channels go
            _disposed = true;

            // dispose proxy channels
            foreach (var channel in _proxyChannels.ToArray())
                RemoveChannelInternal(channel, dispose: true);

            // dispose packet channels
            foreach (var channel in _packetChannels.ToArray())
                RemoveChannelInternal(channel, dispose: true);

            // dispose all disposing channels
            foreach (var channel in _disposingChannels) {
                if (channel is IPacketChannel packetChannel)
                    packetChannel.PacketReceived -= _channelPacketReceived;
                channel.Dispose();
            }

            _disposingChannels.Clear();
        }

        _cleanupJob.Dispose();
    }
}