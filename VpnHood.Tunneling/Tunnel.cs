using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class Tunnel : IDisposable
    {
        private readonly Queue<IPPacket> _packetQueue = new();
        private readonly int _maxQueueLengh = 100;
        private readonly EventWaitHandle _packetQueueAddedEvent = new(false, EventResetMode.ManualReset);
        private readonly EventWaitHandle _packetQueueRemovedEvent = new(false, EventResetMode.AutoReset);
        private readonly object _channelListObject = new();
        private readonly object _packetQueueLock = new();
        private readonly Timer _timer;
        private readonly int _mtuWithFragment = TunnelUtil.MtuWithFragmentation;
        private readonly int _mtuNoFragment = TunnelUtil.MtuWithoutFragmentation;

        private ILogger Logger => VhLogger.Instance;
        public IChannel[] StreamChannels { get; private set; } = new IChannel[0];
        public IDatagramChannel[] DatagramChannels { get; private set; } = new IDatagramChannel[0];

        private long _receivedByteCount;
        public long ReceivedByteCount => _receivedByteCount + StreamChannels.Sum(x => x.ReceivedByteCount) + DatagramChannels.Sum(x => x.ReceivedByteCount);

        private long _sentByteCount;
        public long SentByteCount => _sentByteCount + StreamChannels.Sum(x => x.SentByteCount) + DatagramChannels.Sum(x => x.SentByteCount);

        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketReceived;
        public event EventHandler<ChannelEventArgs> OnChannelAdded;
        public event EventHandler<ChannelEventArgs> OnChannelRemoved;
        public event EventHandler OnTrafficChanged;

        private readonly ConcurrentQueue<long> _sentBytes = new();
        private readonly ConcurrentQueue<long> _receivedBytes = new();
        private const int SpeedThreshold = 2;
        private long _lastSentByteCount = 0;
        private long _lastReceivedByteCount = 0;
        public long SendSpeed => _sentBytes.Sum() / SpeedThreshold;
        public long ReceiveSpeed => _receivedBytes.Sum() / SpeedThreshold;

        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public Tunnel()
        {
            _timer = new Timer(SpeedMonitor, null, 0, 1000);
        }

        private void SpeedMonitor(object state)
        {
            if (_disposed) return;

            var sentByteCount = SentByteCount;
            var receivedByteCount = ReceivedByteCount;
            var trafficChanged = _lastSentByteCount != sentByteCount || _lastReceivedByteCount != receivedByteCount;

            // add transferred bytes
            _sentBytes.Enqueue(sentByteCount - _lastSentByteCount);
            _receivedBytes.Enqueue(receivedByteCount - _lastReceivedByteCount);
            if (_sentBytes.Count > SpeedThreshold) _sentBytes.TryDequeue(out _);
            if (_receivedBytes.Count > SpeedThreshold) _receivedBytes.TryDequeue(out _);

            // save last traffic
            _lastSentByteCount = sentByteCount;
            _lastReceivedByteCount = receivedByteCount;

            // send traffic changed
            if (trafficChanged)
            {
                LastActivityTime = DateTime.Now;
                OnTrafficChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool IsChannelExists(IChannel channel)
        {
            return (channel is IDatagramChannel)
                ? DatagramChannels.Contains(channel)
                : StreamChannels.Contains(channel);
        }

        public void AddChannel(IChannel channel)
        {
            if (_disposed)
                throw new ObjectDisposedException(typeof(Tunnel).Name);

            // add to channel list
            lock (_channelListObject)
            {
                if (IsChannelExists(channel))
                    throw new Exception($"{VhLogger.FormatTypeName(channel)} already exists in the collection!");

                // register finish
                channel.OnFinished += Channel_OnFinished;
                channel.Start();

                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketReceived += Channel_OnPacketReceived;
                    DatagramChannels = DatagramChannels.Concat(new IDatagramChannel[] { datagramChannel }).ToArray();
                    var thread = new Thread(SendPacketThread, TunnelUtil.SocketStackSize_Datagram);
                    thread.Start(channel); // start sending after channel started
                    Logger.LogInformation(GeneralEventId.DatagramChannel, $"A {channel.GetType().Name} has been added. ChannelCount: {DatagramChannels.Length}");
                }
                else
                {
                    StreamChannels = StreamChannels.Concat(new IChannel[] { channel }).ToArray();
                    Logger.LogInformation(GeneralEventId.StreamChannel, $"A {channel.GetType().Name} has been added. ChannelCount: {StreamChannels.Length}");
                }

            }

            // notify channel has been added
            OnChannelAdded?.Invoke(this, new ChannelEventArgs() { Channel = channel });
        }

        public void RemoveChannel(IChannel channel, bool dispose = true)
        {
            lock (_channelListObject)
            {
                if (!IsChannelExists(channel))
                    return; // channel already removed or does not exist

                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketReceived -= OnPacketReceived;
                    DatagramChannels = DatagramChannels.Where(x => x != channel).ToArray();
                    Logger.LogInformation(GeneralEventId.DatagramChannel, $"A {channel.GetType().Name} has been removed. ChannelCount: {DatagramChannels.Length}");
                }
                else
                {
                    StreamChannels = StreamChannels.Where(x => x != channel).ToArray();
                    Logger.LogInformation(GeneralEventId.StreamChannel, $"A {channel.GetType().Name} has been removed. ChannelCount: {StreamChannels.Length}");
                }

                // add stats of dead channel
                _sentByteCount += channel.SentByteCount;
                _receivedByteCount += channel.ReceivedByteCount;
                channel.OnFinished -= Channel_OnFinished;
            }

            // notify channel has been removed
            OnChannelRemoved?.Invoke(this, new ChannelEventArgs() { Channel = channel });

            // dispose
            if (dispose)
                channel.Dispose();
        }

        private void Channel_OnFinished(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            RemoveChannel((IChannel)sender, true);
        }

        private void Channel_OnPacketReceived(object sender, ChannelPacketArrivalEventArgs e)
        {
            if (_disposed)
                return;

            if (VhLogger.IsDiagnoseMode)
                TunnelUtil.LogPackets(e.IpPackets, "dequeued");

            try
            {
                OnPacketReceived?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, $"Packets dropped! Error in processing channel received packets. Message: {ex}");
            }
        }

        public void SendPacket(IPPacket ipPacket) => SendPacket(new[] { ipPacket });

        public void SendPacket(IEnumerable<IPPacket> ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(typeof(Tunnel).Name);

            // waiting for a space in the packetQueue
            while (_packetQueue.Count > _maxQueueLengh)
            {
                _packetQueueAddedEvent.Set(); // We have some packet! 
                _packetQueueRemovedEvent.WaitOne(1000); //Wait 1000 to prevent dead lock.
            }

            // add all packets to the queue
            lock (_packetQueueLock)
            {
                foreach (var ipPacket in ipPackets)
                    _packetQueue.Enqueue(ipPacket);
                _packetQueueAddedEvent.Set();
            }

            if (VhLogger.IsDiagnoseMode)
                TunnelUtil.LogPackets(ipPackets, "enqueued");
        }

        private void SendPacketThread(object obj)
        {
            var channel = (IDatagramChannel)obj;
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
                        while (_packetQueue.TryPeek(out IPPacket ipPacket))
                        {
                            var packetSize = ipPacket.TotalPacketLength;

                            // drop packet if it is larger than _mtuWithFragment
                            if (packetSize > _mtuWithFragment)
                            {
                                Logger.LogWarning($"Packet dropped! There is no channel to support this fragmented packet. Fragmented MTU: {_mtuWithFragment}, PacketLength: {ipPacket.TotalLength}, Packet: {ipPacket}");
                                _packetQueue.TryDequeue(out ipPacket);
                                continue;
                            }

                            // drop packet if it is larger than _mtuNoFragment
                            if (packetSize > _mtuNoFragment && ipPacket is IPv4Packet ipV4packet && ipV4packet.FragmentFlags == 2)
                            {
                                Logger.LogWarning($"Packet dropped! There is no channel to support this non fragmented packet. NoFragmented MTU: {_mtuNoFragment}, PacketLength: {ipPacket.TotalLength}, Packet: {ipPacket}");
                                _packetQueue.TryDequeue(out ipPacket);
                                var replyPacket = PacketUtil.CreateUnreachableReply(ipPacket, IcmpV4TypeCode.UnreachableFragmentationNeeded, (ushort)(_mtuNoFragment));
                                OnPacketReceived?.Invoke(this, new ChannelPacketArrivalEventArgs(new[] { replyPacket }, channel));
                                continue;
                            }

                            // just send this packet if it is bigger than _mtuNoFragment and there is no more packet in the buffer
                            if (packetSize > _mtuNoFragment)
                            {
                                if (packets.Count() == 0 && _packetQueue.TryDequeue(out ipPacket))
                                {
                                    size += packetSize;
                                    packets.Add(ipPacket);
                                }
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
                        channel.SendPackets(packets);
                    }

                    // wait for next new packets
                    lock (_packetQueueLock)
                        if (_packetQueue.Count == 0)
                            _packetQueueAddedEvent.Reset();

                    _packetQueueAddedEvent.WaitOne();
                } // while
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not send {packets.Count} packets via a channel! Message: {ex.Message}");
            }

            // make sure channel has removed
            RemoveChannel(channel, true);
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var channel in StreamChannels)
                channel.Dispose();
            StreamChannels = new IChannel[0]; //cleanup main consuming memory objects faster

            foreach (var channel in DatagramChannels)
                channel.Dispose();
            DatagramChannels = new IDatagramChannel[0]; //cleanup main consuming memory objects faster

            _timer.Dispose();

            // release worker threads
            _packetQueueAddedEvent.Set();
            _packetQueueAddedEvent.Dispose();
            _packetQueueRemovedEvent.Set();
            _packetQueueRemovedEvent.Dispose();
        }
    }
}
