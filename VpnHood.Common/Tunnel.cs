using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood
{

    public class Tunnel : IDisposable
    {
        private readonly Queue<IPPacket> _packetQueue = new Queue<IPPacket>();
        private readonly int _maxQueueLengh = 10000;
        private readonly EventWaitHandle _newPacketEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        private readonly object _lockObject = new object();
        private ILogger Logger => Loggers.Logger.Current;
        
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
        public Timer _timer;

        public Tunnel()
        {
            _timer = new Timer(SpeedMonitor, null, 0, 1000);
        }

        private readonly Queue<long> _sentBytes = new Queue<long>();
        private readonly Queue<long> _receivedBytes = new Queue<long>();
        private const int SpeedThreshold = 5;
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        private void SpeedMonitor(object state)
        {
            if (_disposed) return;

            var sentByteCount = SentByteCount;
            var receivedByteCount = ReceivedByteCount;
            var trafficChanged = 
                (_sentBytes.TryPeek(out long lastSentBytes) && lastSentBytes != sentByteCount) ||
                (_receivedBytes.TryPeek(out long lastReceivedBytes) && lastReceivedBytes != receivedByteCount);

            // add transferred bytes
            _sentBytes.Enqueue(SentByteCount - lastSentBytes);
            _receivedBytes.Enqueue(ReceivedByteCount - receivedByteCount);

            if (_sentBytes.Count > SpeedThreshold) _sentBytes.Dequeue();
            if (_receivedBytes.Count > SpeedThreshold) _receivedBytes.Dequeue();

            // send traffic changed
            if (trafficChanged)
            {
                LastActivityTime = DateTime.Now;
                OnTrafficChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void AddChannel(IChannel channel)
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
                var thread = new Thread(SendPacketThread, Util.SocketStackSize_Datagram);
                thread.Start(channel);
            }

            // log
            var count = (channel is IDatagramChannel) ? DatagramChannels.Length : StreamChannels.Length;
            Logger.LogInformation($"A {channel.GetType().Name} has been added. ChannelCount: {count}");

            // notify channel has been added
            OnChannelAdded?.Invoke(this, new ChannelEventArgs() { Channel = channel });
        }

        // it is private method because caller can not remove the channel since a thread working on it
        private void RemoveChannel(IChannel channel)
        {
            lock (_lockObject)
            {
                if (channel is IDatagramChannel)
                    DatagramChannels = DatagramChannels.Where(x => x != channel).ToArray();
                else
                    StreamChannels = StreamChannels.Where(x => x != channel).ToArray();

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

            OnPacketArrival?.Invoke(sender, e);
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
                    //only one thread can dequeue packets so buffer will send with sequential packets
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

                // re-add the missed packets
                if (!_disposed)
                {
                    foreach (var packet in packets)
                        _packetQueue.Enqueue(packet);
                }
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

        public void SendPacket(IPPacket packet)
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                if (_packetQueue.Count > _maxQueueLengh)
                    throw new Exception("Packet queue is full");

                _packetQueue.Enqueue(packet);
                _newPacketEvent.Set();
            }
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

            foreach (var channel in StreamChannels.ToArray()) // ToArray is required to prevent error on removing channel
                channel.Dispose();

            foreach (var channel in DatagramChannels.ToArray()) // ToArray is required to prevent error on removing channel
                channel.Dispose();

            _timer.Dispose();

            lock (_lockObject)
            {
                _packetQueue.Clear();
                _newPacketEvent.Set();
            }
        }
    }
}
