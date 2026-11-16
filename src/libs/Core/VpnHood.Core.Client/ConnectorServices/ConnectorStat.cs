namespace VpnHood.Core.Client.ConnectorServices;

/// <summary>Represents the statistics of the client's connections to the server in a session.</summary>
public class ConnectorStat
{
    private readonly Func<int> _freeConnectionCountFunc;

    internal ConnectorStat(Func<int> freeConnectionCountFunc)
    {
        _freeConnectionCountFunc = freeConnectionCountFunc;
    }

    /// <summary>Gets how many idle connections the client keeps for reuse now.</summary>
    public int FreeConnectionCount => _freeConnectionCountFunc();

    /// <summary>
    /// Gets how many times a kept connection failed when the client reused it, but by the server's error reply or a
    /// cancel.
    /// </summary>
    public int ReusedConnectionFailedCount { get; internal set; }

    /// <summary>Gets how many times a kept connection carried a request to an answer without an error.</summary>
    public int ReusedConnectionSucceededCount { get; internal set; }

    /// <summary>Gets how many request connections the client made to the server, its recovery probes aside.</summary>
    public int CreatedConnectionCount { get; internal set; }

    /// <summary>Gets how many requests the server answered without an error.</summary>
    public int RequestCount { get; internal set; }
}
