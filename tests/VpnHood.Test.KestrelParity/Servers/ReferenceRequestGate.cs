using System.Diagnostics.Metrics;
using System.Net;
using System.Text;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class ReferenceRequestGate : IDisposable
{
    public const string MarkerName = "x-parity-trailers-parsed";
    private const string MeterName = "Microsoft.AspNetCore.Server.Kestrel";
    private const string InstrumentName = "kestrel.queued_requests";

    private readonly IPEndPoint _endPoint;
    private readonly TimeSpan _timeout;
    private readonly MeterListener _listener = new();
    private ReferenceRequestGateSession? _active;
    private Exception? _instrumentError;
    private int _disposed;

    public ReferenceRequestGate(IPEndPoint endPoint, TimeSpan timeout)
    {
        _endPoint = endPoint;
        _timeout = timeout;
        _listener.InstrumentPublished = InstrumentPublished;
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.Start();
    }

    public ReferenceRequestGateSession Arm(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _instrumentError) is { } error)
            throw new InvalidOperationException($"Could not observe {InstrumentName} request dispatch.", error);
        var session = new ReferenceRequestGateSession(MarkerName, _timeout, Disarm, cancellationToken);
        if (Interlocked.CompareExchange(ref _active, session, null) == null)
            return session;
        session.Dispose();
        throw new InvalidOperationException("Only one request can use the dispatch gate at a time.");
    }

    public Encoding? SelectEncoding(string name)
    {
        // RC3 selects trailer encoding after incrementing both shared limit counters.
        Volatile.Read(ref _active)?.OnHeader(name);
        return null;
    }

    private void InstrumentPublished(Instrument instrument, MeterListener listener)
    {
        if (instrument.Meter.Name != MeterName || instrument.Name != InstrumentName)
            return;
        try {
            listener.EnableMeasurementEvents(instrument);
        }
        catch (Exception ex) {
            // Instrument callbacks run inside the server; report their failure from the test instead.
            Volatile.Write(ref _instrumentError, ex);
        }
    }

    private void OnMeasurement(Instrument instrument, long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (measurement != -1 || !MatchesEndPoint(tags))
            return;
        Volatile.Read(ref _active)?.OnDispatch(CancellationToken.None);
    }

    private bool MatchesEndPoint(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var addressMatches = false;
        var portMatches = false;
        var protocolMatches = false;
        foreach (var tag in tags) {
            if (tag.Key == "server.address")
                addressMatches = tag.Value is string address && address == _endPoint.Address.ToString();
            else if (tag.Key == "server.port")
                portMatches = tag.Value is int port && port == _endPoint.Port;
            else if (tag.Key == "network.protocol.version")
                protocolMatches = tag.Value is "3";
        }
        return addressMatches && portMatches && protocolMatches;
    }

    private void Disarm(ReferenceRequestGateSession session) =>
        Interlocked.CompareExchange(ref _active, null, session);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Interlocked.Exchange(ref _active, null)?.Dispose();
        _listener.Dispose();
    }
}
