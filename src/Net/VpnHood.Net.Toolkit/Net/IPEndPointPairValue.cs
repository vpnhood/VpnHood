namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>Represents a flow's source and destination endpoints, as values.</summary>
/// <param name="Source">The source endpoint.</param>
/// <param name="Destination">The destination endpoint.</param>
public readonly record struct IPEndPointPairValue(
    IpEndPointValue Source, 
    IpEndPointValue Destination)
{
    /// <summary>Returns the pair as <c>source-&gt;destination</c>, not redacted.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Source}->{Destination}";
}