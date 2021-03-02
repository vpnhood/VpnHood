using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{

    public class Tunnel : IDisposable
    {
        private readonly Queue<IPPacket> _packetQueue = new Queue<IPPacket>();
        private readonly int _maxQueueLengh = 10000;
        private readonly EventWaitHandle _newPacketEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly object _lockObject = new object();
        private readonly Timer _timer;
        private ILogger Logger => Logging.VhLogger.Current;

        public IChannel[] StreamChannels { get; private set; } = new IChannel[0];
        public IDatagramChannel[] DatagramChannels { get; private set; } = new IDatagramChannel[0];

        private long _receivedByteCount;
        public long ReceivedByteCount => _receivedByteCount + StreamChannels.Sum(x => x.ReceivedByteCount) + DatagramChannels.Sum(x => x.ReceivedByteCount);

        private long _sentByteCount;
        public long SentByteCount => _sentByteCount + StreamChannels.Sum(x => x.SentByteCount) + DatagramChannels.Sum(x => x.SentByteCount);

        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;
        public event EventHandler<ChannelEventArgs> OnChannelAdded;
        public event EventHandler<ChannelEventArgs> OnChannelRemoved;
        public event EventHandler OnTrafficChanged;

        public Tunnel()
        {
            _timer = new Timer(SpeedMonitor, null, 0, 1000);
        }

        private readonly Queue<long> _sentBytes = new Queue<long>();
        private readonly Queue<long> _receivedBytes = new Queue<long>();
        private const int SpeedThreshold = 5;
        private long _lastSentByteCount = 0;
        private long _lastReceivedByteCount = 0;

        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        private void SpeedMonitor(object state)
        {
            if (_disposed) return;

            var sentByteCount = SentByteCount;
            var receivedByteCount = ReceivedByteCount;
            var trafficChanged = _lastSentByteCount != sentByteCount || _lastReceivedByteCount != receivedByteCount;

            // add transferred bytes
            _sentBytes.Enqueue(sentByteCount - _lastSentByteCount);
            _receivedBytes.Enqueue(receivedByteCount - _lastReceivedByteCount);
            if (_sentBytes.Count > SpeedThreshold) _sentBytes.Dequeue();
            if (_receivedBytes.Count > SpeedThreshold) _receivedBytes.Dequeue();

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

        public void AddChannel(IChannel channel, EventId eventId)
        {
            ThrowIfDisposed();

            var datagramChannel = channel as IDatagramChannel;

            // add to channel list
            lock (_lockObject)
            {
                if (channel is IDatagramChannel)
                    DatagramChannels = DatagramChannels.Concat(new IDatagramChannel[] { datagramChannel }).ToArray();
                else
                    StreamChannels = StreamChannels.Concat(new IChannel[] { channel }).ToArray();
            }

            // register finish
            channel.OnFinished += Channel_OnFinished;

            // start sending packet by this channel
            if (datagramChannel != null)
                datagramChannel.OnPacketArrival += Channel_OnPacketArrival;

            // starting the channel
            channel.Start();

            // start sending after channel started
            if (datagramChannel != null)
            {
                var thread = new Thread(SendPacketThread, TunnelUtil.SocketStackSize_Datagram);
                thread.Start(channel);
            }

            // log
            var count = (channel is IDatagramChannel) ? DatagramChannels.Length : StreamChannels.Length;
            Logger.LogInformation(eventId, $"A {channel.GetType().Name} has been added. ChannelCount: {count}");

            // notify channel has been added
            OnChannelAdded?.Invoke(this, new ChannelEventArgs() { Channel = channel });
        }

        // it is private method because caller can not remove the channel since a thread working on it
        private void RemoveChannel(IChannel channel)
        {
            lock (_lockObject)
            {
                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketArrival -= OnPacketArrival;
                    DatagramChannels = DatagramChannels.Where(x => x != channel).ToArray();
                }
                else
                {
                    StreamChannels = StreamChannels.Where(x => x != channel).ToArray();
                }

                // add stats of dead channel
                _sentByteCount += channel.SentByteCount;
                _receivedByteCount += channel.ReceivedByteCount;

                // report
                var count = (channel is IDatagramChannel) ? DatagramChannels.Length : StreamChannels.Length;
                Logger.LogInformation($"A {channel.GetType().Name} has been removed. ChannelCount: {count}");

                channel.OnFinished -= Channel_OnFinished;
            }

            channel.Dispose();

            // notify channel has been removed
            OnChannelRemoved?.Invoke(this, new ChannelEventArgs() { Channel = channel });
        }

        private void Channel_OnFinished(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            RemoveChannel((IChannel)sender);
        }

        private void Channel_OnPacketArrival(object sender, ChannelPacketArrivalEventArgs e)
        {
            if (_disposed)
                return;

            // log ICMP
            if (VhLogger.IsDiagnoseMode && e.IpPacket.Protocol == ProtocolType.Icmp)
            {
                var icmpPacket = e.IpPacket.Extract<IcmpV4Packet>();
                VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"ICMP has been received from a channel! DestAddress: {e.IpPacket.DestinationAddress}, DataLen: {icmpPacket.Data.Length}, Data: {BitConverter.ToString(icmpPacket?.Data, 0, Math.Min(10, icmpPacket.Data.Length))}.");
            }

            OnPacketArrival?.Invoke(sender, e);
        }
        public void SendPacket(IPPacket ipPacket)
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                if (_packetQueue.Count > _maxQueueLengh)
                    throw new Exception("Packet queue is full");

                // log ICMP
                if (VhLogger.IsDiagnoseMode && ipPacket.Protocol == ProtocolType.Icmp)
                {
                    var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                    VhLogger.Current.Log(LogLevel.Information, GeneralEventId.Ping, $"ICMP had been enqueued to a channel! DestAddress: {ipPacket.DestinationAddress}, DataLen: {icmpPacket.Data.Length}, Data: {BitConverter.ToString(icmpPacket.Data, 0, Math.Min(10, icmpPacket.Data.Length))}.");
                }

                _packetQueue.Enqueue(ipPacket);
                _newPacketEvent.Set();
            }
        }

        private void SendPacketThread(object obj)
        {
            var channel = (IDatagramChannel)obj;

            var packets = new List<IPPacket>();
            var sendBufferSize = channel.SendBufferSize;

            // ** Warning: This is the most busy lopp in the app. Perfomance is critical!
            try
            {
                while (channel.Connected)
                {
                    //only one thread can dequeue packets to let send buffer with sequential packets
                    // dequeue available packets and add them to list in favour of buffer size
                    lock (_lockObject)
                    {
                        var size = 0;
                        packets.Clear();
                        while (_packetQueue.TryPeek(out IPPacket packet))
                        {
                            var packetSize = packet.TotalPacketLength;
                            if (packet.TotalPacketLength + size > sendBufferSize)
                                break;

                            size = packetSize + size;
                            if (_packetQueue.TryDequeue(out packet))
                                packets.Add(packet);
                        }
                    }

                    // singal that there are more packets
                    lock (_lockObject)
                    {
                        if (_packetQueue.Count > 0)
                            _newPacketEvent.Set();
                    }

                    // send selected packets
                    if (packets.Count > 0)
                        channel.SendPacket(packets.ToArray());

                    // wait next packet signal
                    _newPacketEvent.WaitOne();
                } // while
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not send {packets.Count} packets via a channel! Message: {ex.Message}");
            }
            finally
            {
                // this channel is finished, ether by exception or disconnection. Let other do the job!
                _newPacketEvent.Set();
            }

            // make sure channel is removed
            if (DatagramChannels.Any(x => x == channel))
                RemoveChannel(channel);
        }

        private void ThrowIfDisposed()
        {
            lock (_lockObject)
                if (_disposed)
                    throw new ObjectDisposedException(typeof(Tunnel).Name);
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

            lock (_lockObject)
            {
                _packetQueue.Clear();
                _newPacketEvent.Set();
            }
        }
    }
}
