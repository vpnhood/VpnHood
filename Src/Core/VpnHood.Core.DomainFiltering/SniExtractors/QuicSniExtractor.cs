using System.Collections.Concurrent;
using VpnHood.Core.DomainFiltering.SniExtractors.Quic;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniExtractors;

public class QuicSniExtractor : IPacketSniExtractor
{
    private readonly ConcurrentDictionary<IpEndPointValue, QuicSniState> _flowStates = new();
    private readonly TimeSpan _stateTimeout;

    public QuicSniExtractor(TimeSpan? stateTimeout = null)
    {
        _stateTimeout = stateTimeout ?? TimeSpan.FromSeconds(5);
    }

    public QuicSniResultNew ExtractSni(IpPacket ipPacket)
    {
        // Only process UDP packets
        if (ipPacket.Protocol != IpProtocol.Udp)
            return new QuicSniResultNew { DomainName = null, NeedMore = false, State = null };

        // Get UDP payload
        var udpPacket = ipPacket.PayloadPacket as UdpPacket;
        if (udpPacket == null)
            return new QuicSniResultNew { DomainName = null, NeedMore = false, State = null };

        var udpPayload = udpPacket.Payload.Span;

        // Create flow key from source endpoint
        var flowKey = new IpEndPointValue(ipPacket.SourceAddress, udpPacket.SourcePort);

        // Get or create state for this flow
        var state = _flowStates.TryGetValue(flowKey, out var existingState) ? existingState : null;

        // Extract SNI using the stateful QUIC extractor
        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var result = QuicSniExtractorStateful.TryExtractSniFromUdpPayload(udpPayload, state, nowTicks);

        if (result.DomainName != null) {
            _flowStates.TryRemove(flowKey, out _);
            return new QuicSniResultNew {
                DomainName = result.DomainName,
                NeedMore = false,
                State = null
            };
        }

        if (result.NeedMore) {
            if (result.State != null)
                _flowStates[flowKey] = result.State;
            return new QuicSniResultNew {
                DomainName = null,
                NeedMore = true,
                State = result.State
            };
        }

        _flowStates.TryRemove(flowKey, out _);
        return new QuicSniResultNew {
            DomainName = null,
            NeedMore = false,
            State = null
        };
    }

    public void CleanupExpiredStates()
    {
        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var expiredKeys = new List<IpEndPointValue>();

        foreach (var kvp in _flowStates) {
            if (nowTicks > kvp.Value.DeadlineTicks)
                expiredKeys.Add(kvp.Key);
        }

        foreach (var key in expiredKeys)
            _flowStates.TryRemove(key, out _);
    }
}
