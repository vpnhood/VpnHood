using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors.TlsStream;
using VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering;

/// <summary>
/// Provides the client's filtering of flows by their domain names, which it reads from their TLS server names (SNI):
/// a QUIC flow's from its first packets, and a TCP connection's from the first bytes of its stream.
/// </summary>
/// <remarks>
/// A TCP connection's name comes after its handshake, too late to exclude it from its packets, so TCP is filtered by
/// its stream alone, which the client's TCP forwarding hands over. The service filters nothing while
/// <see cref="IsEnabled"/> is <see langword="false"/>.
/// </remarks>
public class DomainFilteringService : IDisposable
{
    private static readonly TimeSpan UdpFlowTimeout = TimeSpan.FromMinutes(2);

    private readonly IDomainFilter _domainFilter;
    private readonly QuicSniFilteringService _quicSniService; // Quic only, don't try Tcp SNI extraction by packet
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;

    /// <summary>
    /// Gets the record of the domain names seen, which is kept only when the service was made to track them.
    /// </summary>
    public DomainObserver DomainObserver { get; }

    /// <summary>Gets the counts of the TCP connections whose name was found, by verdict.</summary>
    public DomainObserverStat TcpStat { get; } = new();

    /// <summary>Gets the counts of the QUIC flows whose name was found, by verdict.</summary>
    public DomainObserverStat QuicStat { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainFilteringService"/> class.
    /// </summary>
    /// <param name="domainFilter">The domain filter pipe that decides each name's verdict.</param>
    /// <param name="sniEventId">
    /// The event ID of the log entries of the names found: each QUIC flow's, and each name the observer records.
    /// </param>
    /// <param name="tlsBufferSize">
    /// The size of the one read that takes a TCP stream's first bytes, which must hold its TLS ClientHello.
    /// </param>
    /// <param name="trackObservations">
    /// <see langword="true"/> to record each name in <see cref="DomainObserver"/>.
    /// </param>
    public DomainFilteringService(
        IDomainFilter domainFilter,
        EventId sniEventId,
        int tlsBufferSize,
        bool trackObservations = false)
    {
        _domainFilter = domainFilter;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _quicSniService = new QuicSniFilteringService(_domainFilter, flowTimeout: UdpFlowTimeout, sniEventId: sniEventId);
        DomainObserver = new DomainObserver(sniEventId);
    }

    /// <summary>Occurs when <see cref="IsEnabled"/> changes.</summary>
    // a session's TCP forwarding, which a domain rule needs for a TCP connection's name, follows this (ClientSession)
    public event EventHandler? IsEnabledChanged;

    /// <summary>
    /// Gets or sets a value indicating whether the service filters. The default is <see langword="false"/>.
    /// </summary>
    public bool IsEnabled {
        get;
        set {
            if (field == value) return;
            field = value;
            IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Decides what happens to a UDP packet by the server name of its QUIC flow, holding the flow's first packets back
    /// until the name is found or given up on.
    /// </summary>
    /// <param name="ipPacket">The packet the device sent.</param>
    /// <returns>
    /// The verdict for the packet and for the flow's packets held before it, which the result releases; a result
    /// with <see cref="PacketSniFilterResult.NeedMore"/> when the service holds the packet. A packet of another
    /// protocol, or any packet while the service is off, gets <see cref="FilterAction.Default"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A flow is known by the packet's source address and port alone, whatever its destination: once decided, its
    /// verdict answers every later packet from them, until a periodic sweep finds the flow unused for two minutes. A
    /// flow whose first packets are not QUIC gets <see cref="FilterAction.Default"/>. A held packet is the service's
    /// until a later result releases it: route nothing for a result that needs more, and route the released packets
    /// before the packet given. A flow forgotten with packets held leaves them undisposed.
    /// </para>
    /// <para>
    /// A flow whose name is found counts in <see cref="QuicStat"/>, and is recorded in <see cref="DomainObserver"/>
    /// when the service tracks names.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The packet is disposed and the service is on, or the service is disposed and the packet is UDP while it is on.
    /// </exception>
    /// <exception cref="InvalidDataException">The UDP packet is a fragment.</exception>
    /// <exception cref="ArgumentException">The UDP packet is malformed.</exception>
    public PacketSniFilterResult ProcessPacket(IpPacket ipPacket)
    {
        if (!IsEnabled)
            return PacketSniFilterResult.Passthrough();

        var result = ipPacket.Protocol switch {
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketSniFilterResult.Passthrough()
        };

        // Track observation if enabled
        if (!result.IsNewFlow || string.IsNullOrEmpty(result.DomainName)) 
            return result;

        // Update QUIC stat
        QuicStat.Update(result.Action);

        // For QUIC, we can only get remote endpoint from packet, so we use destination endpoint for tracking
        if (_trackObservations) {
            DomainObserver.Track(result.DomainName,
                result.Action, DomainObservationProtocol.Quic, ipPacket.GetDestinationEndPoint());
        }

        return result;
    }


    /// <summary>
    /// Decides what happens to a TCP connection by the server name in the TLS ClientHello its stream begins with.
    /// </summary>
    /// <param name="tlsStream">The connection's stream, from the device, not read yet.</param>
    /// <param name="remoteEndPoint">
    /// The connection's destination, which is logged with the name when the service tracks names.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result holds the verdict, the name, and the bytes read from the stream, which the caller must send
    /// on before the rest of it. While the service is off, nothing is read and the verdict is
    /// <see cref="FilterAction.Default"/>.
    /// </returns>
    /// <remarks>
    /// It reads from the stream once, up to the buffer size the service was made with, and looks for the name in what
    /// that read returned: a stream with no name there, as when the ClientHello comes in pieces, gets the verdict of a
    /// null name. A name found counts in <see cref="TcpStat"/>, and is recorded in <see cref="DomainObserver"/> when
    /// the service tracks names.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<StreamSniFilterResult> ProcessStream(Stream tlsStream, IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return StreamSniFilterResult.Passthrough();

        // extract SNI
        var sniData = await StreamSniExtractor.ExtractSni(tlsStream, _sniEventId, _tlsBufferSize, cancellationToken).Vhc();

        // no SNI
        var res = new StreamSniFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = _domainFilter.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (!string.IsNullOrEmpty(res.DomainName)) {
            // Update stream stat
            TcpStat.Update(res.Action);

            // For TCP, we can get remote endpoint from stream, so we use it for tracking
            if (_trackObservations)
                DomainObserver.Track(res.DomainName, res.Action, DomainObservationProtocol.Tcp, remoteEndPoint.ToValue());
        }

        return res;
    }

    /// <summary>Releases the record of the QUIC flows.</summary>
    public void Dispose()
    {
        _quicSniService.Dispose();
    }
}