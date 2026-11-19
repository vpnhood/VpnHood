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

    // Long enough for a slow device's ClientHello, whose name a shorter wait would miss, so its connection would pass
    // the domain rules; short enough for a protocol in which the server speaks first, whose start waits it out
    private static readonly TimeSpan StreamSniTimeout = TimeSpan.FromSeconds(1);

    private readonly IDomainFilter _domainFilter;
    private readonly QuicSniFilteringService _quicSniService; // Quic only, don't try Tcp SNI extraction by packet
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
    /// The most bytes read from the start of a TCP stream for its name, which must hold its TLS ClientHello.
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
    /// A flow is known by the packet's source and its destination, each an address and a port. It has one verdict,
    /// which answers every packet that begins no connection, until a sweep, made as packets come, finds the flow
    /// unused for two minutes. A flow whose first packet begins no QUIC connection, as any UDP but QUIC, gets
    /// <see cref="FilterAction.Default"/> at once. A connection begins with a client Initial of QUIC version 1 or 2,
    /// in a datagram of 1200 bytes or more, that decrypts with the keys of its own connection ID. Its first packets
    /// stay held until its name is read from them: a whole ClientHello with no name gets
    /// <see cref="FilterAction.Default"/>, and one not complete within sixteen datagrams, as many as a ClientHello of
    /// 16 KB takes, or by a datagram that comes after a second, gets <see cref="FilterAction.Block"/> for the packets
    /// held, as a lost or late part would otherwise let its name pass the rules; sent again, it is read again. So
    /// does one that is whole and not well formed, a handshake that begins with no ClientHello, and a read that has
    /// held sixty-four packets. Where the domain filter holds no rule, as when the service is on for its log alone,
    /// no name could change the verdict, so a name that cannot be read gets <see cref="FilterAction.Default"/>.
    /// </para>
    /// <para>
    /// Another connection that begins on the flow, while the one before it lives or after it, as a client that keeps
    /// its socket opens to the same server, has its own name read the same way: an Initial under another connection
    /// ID than that of the one read last. A blocked one is dropped at its first packets, each time they come, and
    /// never gets past them, so its block leaves the flow's verdict to the others. Any other verdict joins the
    /// flow's: past their first packets the connections of a flow are not told apart, so the flow's verdict is the
    /// most protective of theirs, <see cref="FilterAction.Include"/> before <see cref="FilterAction.Default"/> before
    /// <see cref="FilterAction.Exclude"/>, and none leaves the tunnel by another's exclude. The connection's first
    /// packets leave with its own verdict, so the caller's IP rules still judge one with no entry; an excluded one's
    /// alone leave with the flow's, as its later packets will. A connection that begins while another is being read
    /// takes the read, and the packets held until then are forgotten.
    /// </para>
    /// <para>
    /// A held packet is the service's until a later result releases it: route nothing for a result that needs more,
    /// and route the released packets before the packet given. A flow forgotten with packets held leaves them
    /// undisposed.
    /// </para>
    /// <para>
    /// A connection whose name is found counts in <see cref="QuicStat"/>, and is recorded in
    /// <see cref="DomainObserver"/> when the service tracks names.
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
    /// It reads the stream's first bytes, up to the buffer size the service was made with: one read, and, where they
    /// begin a TLS ClientHello, more reads until all the records that hold it are in, as a large ClientHello comes in
    /// pieces and may take more than one record. The reads have one second in all. A stream that has sent nothing by
    /// then, as one of a protocol in which the server speaks first (SMTP, FTP, MySQL), gets the verdict of a null name
    /// with no bytes read, so its start waits that second out. A ClientHello begun and not complete by then, or when
    /// the stream ends, the buffer is full or a record of another type comes, fails the call, as its name may be in
    /// the part not read; so does one that is whole and not well formed. Where the domain filter holds no rule, as
    /// when the service is on for its log alone, no name could change the verdict, so such a stream fails nothing and
    /// gets the verdict of a null name with the bytes read. A stream with no name in its bytes gets the verdict of a
    /// null name. A name found counts in <see cref="TcpStat"/>, and is recorded in <see cref="DomainObserver"/> when
    /// the service tracks names.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">
    /// The stream began a TLS ClientHello and did not complete it within the second, so its name is not known, and the
    /// filter holds a rule.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The stream began a TLS ClientHello that it ended, that outgrew the buffer, or that a record of another type
    /// cut, before it was complete, or one that is not well formed, and the filter holds a rule.
    /// </exception>
    public async Task<StreamSniFilterResult> ProcessStream(Stream tlsStream, IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return StreamSniFilterResult.Passthrough();

        // extract SNI
        var sniData = await StreamSniExtractor.ExtractSni(tlsStream, _tlsBufferSize, StreamSniTimeout,
            cancellationToken).Vhc();

        // A name that could not be read fails the connection, as it may be one a rule matches. Where the filter holds
        // no rule, every name's verdict is Default, and so is this one's: the connection goes on with what was read
        if (sniData.Error != null && !_domainFilter.IsEmpty)
            throw sniData.Error;

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