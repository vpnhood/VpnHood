using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class Tunnel : IDisposable
    {
        private const int SpeedThreshold = 2;
        private readonly object _channelListLock = new();
        private readonly int _maxQueueLengh = 100;
        private readonly int _mtuNoFragment = TunnelUtil.MtuWithoutFragmentation;
        private readonly int _mtuWithFragment = TunnelUtil.MtuWithFragmentation;
        private readonly Queue<IPPacket> _packetQueue = new();
        private readonly object _packetQueueLock = new();
        private readonly EventWaitHandle _packetQueueRemovedEvent = new(false, EventResetMode.AutoReset);
        private readonly SemaphoreSlim _packetQueueSemaphore = new(0);
        private readonly Queue<long> _receivedBytes = new();

        private readonly Queue<long> _sentBytes = new();


        private readonly object _speedMonitorLock = new();
        private readonly HashSet<IChannel> _streamChannels = new();
        private readonly Timer _timer;

        private bool _disposed;
        private long _lastReceivedByteCount;
        private long _lastSentByteCount;
        private int _maxDatagramChannelCount = 1;

        private long _receivedByteCount;

        private long _sentByteCount;

        public Tunnel()
        {
            _timer = new Timer(SpeedMonitor, null, 0, 1000);
        }

        public int StreamChannelCount => _streamChannels.Count();
        public IDatagramChannel[] DatagramChannels { get; private set; } = Array.Empty<IDatagramChannel>();

        public long ReceivedByteCount
        {
            get
            {
                lock (_channelListLock)
                {
                    return _receivedByteCount + _streamChannels.Sum(x => x.ReceivedByteCount) +
                           DatagramChannels.Sum(x => x.ReceivedByteCount);
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                lock (_channelListLock)
                {
                    return _sentByteCount + _streamChannels.Sum(x => x.SentByteCount) +
                           DatagramChannels.Sum(x => x.SentByteCount);
                }
            }
        }

        public long SendSpeed { get; private set; }
        public long ReceiveSpeed { get; private set; }
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public int MaxDatagramChannelCount
        {
            get => _maxDatagramChannelCount;
            set
            {
                if (_maxDatagramChannelCount < 1)
                    throw new ArgumentException("Value must equals or greater than 1", nameof(MaxDatagramChannelCount));
                _maxDatagramChannelCount = value;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_channelListLock)
            {
                foreach (var channel in _streamChannels)
                    channel.Dispose();
                _streamChannels.Clear(); //cleanup main consuming memory objects faster
            }

            foreach (var channel in DatagramChannels)
                channel.Dispose();
            DatagramChannels = new IDatagramChannel[0]; //cleanup main consuming memory objects faster

            _timer.Dispose();

            // release worker threads
            _packetQueueSemaphore.Release(MaxDatagramChannelCount);
            _packetQueueSemaphore.Dispose();
            _packetQueueRemovedEvent.Set();
            _packetQueueRemovedEvent.Dispose();
        }

        public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
        public event EventHandler<ChannelEventArgs>? OnChannelAdded;
        public event EventHandler<ChannelEventArgs>? OnChannelRemoved;
        public event EventHandler? OnTrafficChanged;

        private void SpeedMonitor(object state)
        {
            if (_disposed) return;

            bool trafficChanged;
            lock (_speedMonitorLock)
            {
                var sentByteCount = SentByteCount;
                var receivedByteCount = ReceivedByteCount;
                trafficChanged = _lastSentByteCount != sentByteCount || _lastReceivedByteCount != receivedByteCount;

                // add transferred bytes
                _sentBytes.Enqueue(sentByteCount - _lastSentByteCount);
                _receivedBytes.Enqueue(receivedByteCount - _lastReceivedByteCount);
                if (_sentBytes.Count > SpeedThreshold) _sentBytes.TryDequeue(out _);
                if (_receivedBytes.Count > SpeedThreshold) _receivedBytes.TryDequeue(out _);

                // calculate speed
                SendSpeed = _sentBytes.Sum() / SpeedThreshold;
                ReceiveSpeed = _receivedBytes.Sum() / SpeedThreshold;

                // save last traffic
                _lastSentByteCount = sentByteCount;
                _lastReceivedByteCount = receivedByteCount;
            }

            // fire traffic changed
            if (trafficChanged)
            {
                LastActivityTime = DateTime.Now;
                OnTrafficChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool IsChannelExists(IChannel channel)
        {
            lock (_channelListLock)
            {
                return channel is IDatagramChannel
                    ? DatagramChannels.Contains(channel)
                    : _streamChannels.Contains(channel);
            }
        }

        public void AddChannel(IChannel channel)
        {
            if (_disposed)
                throw new ObjectDisposedException(typeof(Tunnel).Name);

            var datagramChannel = channel as IDatagramChannel;

            // add to channel list
            lock (_channelListLock)
            {
                if (IsChannelExists(channel))
                    throw new Exception($"{VhLogger.FormatTypeName(channel)} already exists in the collection!");

                if (datagramChannel != null)
                {
                    datagramChannel.OnPacketReceived += Channel_OnPacketReceived;
                    DatagramChannels = DatagramChannels.Concat(new[] {datagramChannel}).ToArray();
                    VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                        $"A {VhLogger.FormatTypeName(channel)} has been added. ChannelCount: {DatagramChannels.Length}");

                    // remove additional Datagram channels
                    while (DatagramChannels.Length > MaxDatagramChannelCount)
                    {
                        VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                            $"Removing an exceeded DatagramChannel! ChannelCount: {DatagramChannels.Length}");
                        RemoveChannel(DatagramChannels[0]);
                    }
                }
                else
                {
                    _streamChannels.Add(channel);
                    VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel,
                        $"A {VhLogger.FormatTypeName(channel)} has been added. ChannelCount: {_streamChannels.Count}");
                }
            }

            // register finish
            channel.OnFinished += Channel_OnFinished;

            // notify channel has been added; must before channel.Start because channel may be removed immedietly after channel.Start
            OnChannelAdded?.Invoke(this, new ChannelEventArgs(channel));

            //should not be called in lock; its behaviour is unexpected
            channel.Start();

            //  SendPacketTask after starting the channel and outside of the lock
            if (datagramChannel != null)
                _ = SendPacketTask(datagramChannel);
        }

        public void RemoveChannel(IChannel channel)
        {
            lock (_channelListLock)
            {
                if (!IsChannelExists(channel))
                    return; // channel already removed or does not exist

                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketReceived -= OnPacketReceived;
                    DatagramChannels = DatagramChannels.Where(x => x != channel).ToArray();
                    VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                        $"A {channel.GetType().Name} has been removed. ChannelCount: {DatagramChannels.Length}");
                }
                else
                {
                    _streamChannels.Remove(channel);
                    VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel,
                        $"A {channel.GetType().Name} has been removed. ChannelCount: {_streamChannels.Count}");
                }

                // add stats of dead channel
                _sentByteCount += channel.SentByteCount;
                _receivedByteCount += channel.ReceivedByteCount;
                channel.OnFinished -= Channel_OnFinished;
            }

            // dispose before invoking the event
            // channel may be disposed by itself so let call the invoke always with a disposed channel
            channel.Dispose();

            // notify channel has been removed
            OnChannelRemoved?.Invoke(this, new ChannelEventArgs(channel));
        }

        private void Channel_OnFinished(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            RemoveChannel((IChannel) sender);
        }

        private void Channel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
        {
            if (_disposed)
                return;

            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPackets(e.IpPackets, "dequeued");

            try
            {
                OnPacketReceived?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error,
                    $"Packets dropped! Error in processing channel received packets. Message: {ex}");
            }
        }

        public void SendPacket(IPPacket ipPacket)
        {
            SendPacket(new[] {ipPacket});
        }

        public void SendPacket(IEnumerable<IPPacket> ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(typeof(Tunnel).Name);

            // waiting for a space in the packetQueue
            while (_packetQueue.Count > _maxQueueLengh)
            {
                var releaseCount = MaxDatagramChannelCount - _packetQueueSemaphore.CurrentCount;
                if (releaseCount > 0)
                    _packetQueueSemaphore.Release(releaseCount); // there is some packet! 
                _packetQueueRemovedEvent.WaitOne(1000); //Wait 1000 to prevent dead lock.
            }

            // add all packets to the queue
            lock (_packetQueueLock)
            {
                foreach (var ipPacket in ipPackets)
                    _packetQueue.Enqueue(ipPacket);
                var releaseCount = MaxDatagramChannelCount - _packetQueueSemaphore.CurrentCount;
                if (releaseCount > 0)
                    _packetQueueSemaphore.Release(releaseCount); // there is some packet! 
            }

            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPackets(ipPackets, "enqueued");
        }

        private async Task SendPacketTask(IDatagramChannel channel)
        {
            var packets = new List<IPPacket>();

            // ** Warning: This is the most busy loop in the app. Perfomance is critical!
            try
            {
                while (channel.Connected && !_disposed)
                {
                    //only one thread can dequeue packets to let send buffer with sequential packets
                    // dequeue available packets and add them to list in favour of buffer size
                    lock (_packetQueueLock)
                    {
                        var size = 0;
                        packets.Clear();
                        while (_packetQueue.TryPeek(out var ipPacket))
                        {
                            if (ipPacket == null) throw new Exception("Null packet should not be in the queue!");
                            var packetSize = ipPacket.TotalPacketLength;

                            // drop packet if it is larger than _mtuWithFragment
                            if (packetSize > _mtuWithFragment)
                            {
                                VhLogger.Instance.LogWarning(
                                    $"Packet dropped! There is no channel to support this fragmented packet. Fragmented MTU: {_mtuWithFragment}, PacketLength: {ipPacket.TotalLength}, Packet: {ipPacket}");
                                _packetQueue.TryDequeue(out ipPacket);
                                continue;
                            }

                            // drop packet if it is larger than _mtuNoFragment
                            if (packetSize > _mtuNoFragment && ipPacket is IPv4Packet ipV4packet &&
                                ipV4packet.FragmentFlags == 2)
                            {
                                VhLogger.Instance.LogWarning(
                                    $"Packet dropped! There is no channel to support this non fragmented packet. NoFragmented MTU: {_mtuNoFragment}, PacketLength: {ipPacket.TotalLength}, Packet: {ipPacket}");
                                _packetQueue.TryDequeue(out ipPacket);
                                var replyPacket = PacketUtil.CreateUnreachableReply(ipPacket,
                                    IcmpV4TypeCode.UnreachableFragmentationNeeded, (ushort) _mtuNoFragment);
                                OnPacketReceived?.Invoke(this,
                                    new ChannelPacketReceivedEventArgs(new[] {replyPacket}, channel));
                                continue;
                            }

                            // just send this packet if it is bigger than _mtuNoFragment and there is no more packet in the buffer
                            if (packetSize > _mtuNoFragment)
                                if (packets.Count() == 0 && _packetQueue.TryDequeue(out ipPacket))
                                {
                                    size += packetSize;
                                    packets.Add(ipPacket);
                                }

                            // skip if buffer full
                            if (ipPacket.TotalPacketLength + size > _mtuNoFragment)
                                break;

                            size += packetSize;
                            if (_packetQueue.TryDequeue(out ipPacket))
                                packets.Add(ipPacket);
                        }
                    }

                    // send selected packets
                    if (packets.Count > 0)
                    {
                        _packetQueueRemovedEvent.Set();
                        await channel.SendPacketAsync(packets);
                    }
                    // wait for next new packets
                    else
                    {
                        await _packetQueueSemaphore.WaitAsync();
                    }
                } // while
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(
                    $"Could not send {packets.Count} packets via a channel! Message: {ex.Message}");
            }

            // make sure to remove the channel
            RemoveChannel(channel);
        }
    }
}