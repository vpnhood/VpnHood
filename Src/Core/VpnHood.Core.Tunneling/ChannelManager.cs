using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

internal class ChannelManager : IDisposable, IJob
{
    private readonly object _channelListLock = new();
    private readonly HashSet<IChannel> _disposingChannels = [];
    private readonly HashSet<IProxyChannel> _proxyChannels = [];
    private readonly List<IPacketChannel> _packetChannels = [];
    private int _maxPacketChannelCount;
    private Traffic _trafficUsage = new();
    private readonly EventHandler<IpPacket> _channelPacketReceived;
    private bool _disposed;

    public JobSection JobSection { get; } = new();

    public ChannelManager(int maxPacketChannelCount, EventHandler<IpPacket> channelPacketReceived)
    {
        MaxPacketChannelCount = maxPacketChannelCount;
        _channelPacketReceived = channelPacketReceived;
        JobRunner.Default.Add(this);
    }

    public Traffic Traffic {
        get {
            lock (_channelListLock) {
                var sent = _trafficUsage.Sent;
                var received = _trafficUsage.Received;

                sent += _proxyChannels.Sum(x => x.Traffic.Sent);
                received += _proxyChannels.Sum(x => x.Traffic.Received);

                sent += _packetChannels.Sum(x => x.Traffic.Sent);
                received += _packetChannels.Sum(x => x.Traffic.Received);

                return new Traffic { Sent = sent, Received = received };
            }
        }
    }

    public int MaxPacketChannelCount {
        get => _maxPacketChannelCount;
        set {
            if (value < 1)
                throw new ArgumentException("Value must equals or greater than 1", nameof(MaxPacketChannelCount));
            _maxPacketChannelCount = value;
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

        // remove excess packet channels if needed
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

    public void RemoveChannel(IChannel channel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        switch (channel) {
            case PacketChannel packetChannel:
                packetChannel.PacketReceived -= _channelPacketReceived;
                RemoveChannel(_packetChannels, packetChannel, GeneralEventId.PacketChannel);
                break;

            case IProxyChannel proxyChannel:
                RemoveChannel(_proxyChannels, proxyChannel, GeneralEventId.ProxyChannel);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private void RemoveChannel<T>(ICollection<T> channels, T channel, EventId eventId) where T : IChannel
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
        _trafficUsage += channel.Traffic;
        channel.Dispose();
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
        }
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

    public void Dispose()
    {
        if (_disposed)
            return;

        // remove all channels and dispose them
        lock (_channelListLock) {
            // dispose proxy channels
            foreach (var channel in _proxyChannels)
                channel.Dispose();
            _proxyChannels.Clear();

            // dispose packet channels
            foreach (var channel in _packetChannels) {
                channel.PacketReceived -= _channelPacketReceived;
                channel.Dispose();
            }
            _packetChannels.Clear();

            // dispose all disposing channels
            foreach (var channel in _disposingChannels) {
                if (channel is IPacketChannel packetChannel)
                    packetChannel.PacketReceived -= _channelPacketReceived;
                channel.Dispose();
            }
            _disposingChannels.Clear();
        }

        JobRunner.Default.Remove(this);
        _disposed = true;
    }

    public IPacketChannel GetPacketChannel(int channelIndex)
    {
        lock (_channelListLock) {
            if (channelIndex >= _packetChannels.Count)
                channelIndex = _packetChannels.Count - 1;

            if (channelIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(channelIndex), "Channel index is out of range.");

            return _packetChannels[channelIndex];
        }
    }

    public void RemoveAllPacketChannels()
    {
        foreach (var channel in _packetChannels.ToArray())
            RemoveChannel(channel);
    }
}