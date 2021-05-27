﻿using Microsoft.Extensions.Logging;
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
        private readonly Queue<IPPacket> _packetQueue = new();
        private readonly int _maxQueueLengh = 100;
        private readonly EventWaitHandle _newPacketEvent = new(false, EventResetMode.AutoReset);
        private readonly EventWaitHandle _packetQueueChangedEvent = new(false, EventResetMode.AutoReset);
        private readonly object _lockObject = new();
        private readonly object _sendPacketLock = new();
        private readonly object _packetQueueLock = new();
        private readonly Timer _timer;
        private ILogger Logger => VhLogger.Current;

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

        private readonly Queue<long> _sentBytes = new();
        private readonly Queue<long> _receivedBytes = new();
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

        public void AddChannel(IChannel channel)
        {
            ThrowIfDisposed();


            // add to channel list
            lock (_lockObject)
            {
                // register finish
                channel.OnFinished += Channel_OnFinished;
                channel.Start();

                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketArrival += Channel_OnPacketArrival;
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

        public void RemoveChannel(IChannel channel)
        {
            lock (_lockObject)
            {
                if (channel is IDatagramChannel datagramChannel)
                {
                    datagramChannel.OnPacketArrival -= OnPacketArrival;
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

            if (VhLogger.IsDiagnoseMode)
                TunnelUtil.LogPackets(e.IpPackets, "dequeued");

            OnPacketArrival?.Invoke(sender, e);
        }

        public void SendPacket(IPPacket ipPacket) => SendPacket(new[] { ipPacket });

        public void SendPacket(IPPacket[] ipPackets)
        {
            ThrowIfDisposed();
            if (ipPackets.Length == 0)
                return;

            lock (_sendPacketLock)
            {
                while (_packetQueue.Count > _maxQueueLengh)
                    _packetQueueChangedEvent.WaitOne(); //waiting for a space in the packetQueue

                // add all packets to the queue
                lock (_packetQueueLock)
                {
                    foreach (var ipPacket in ipPackets)
                        _packetQueue.Enqueue(ipPacket);
                    _newPacketEvent.Set();
                }
            }

            if (VhLogger.IsDiagnoseMode)
                TunnelUtil.LogPackets(ipPackets, "enqueued");
        }

        private void SendPacketThread(object obj)
        {
            var channel = (IDatagramChannel)obj;

            var packets = new List<IPPacket>();
            var sendBufferSize = channel.SendBufferSize;

            // ** Warning: This is the most busy lopp in the app. Perfomance is critical!
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
                        while (_packetQueue.TryPeek(out IPPacket packet))
                        {
                            var packetSize = packet.TotalPacketLength;
                            if (packet.TotalPacketLength + size > sendBufferSize)
                                break;

                            size = packetSize + size;
                            if (_packetQueue.TryDequeue(out packet))
                                packets.Add(packet);
                        }

                        // singal that there are more packets
                        if (_packetQueue.Count > 0)
                            _newPacketEvent.Set();
                    }

                    // send selected packets
                    if (packets.Count > 0 && !_disposed)
                    {
                        _packetQueueChangedEvent.Set();
                        channel.SendPackets(packets.ToArray());
                    }

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
                _packetQueueChangedEvent.Set(); // let check queue again
            }

            // make sure channel is removed
            if (DatagramChannels.Any(x => x == channel))
                RemoveChannel(channel);
        }

        private void ThrowIfDisposed()
        {
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

            // free worker threads
            lock (_packetQueueLock)
                _packetQueue.Clear();
            _newPacketEvent.Set();
        }
    }
}
