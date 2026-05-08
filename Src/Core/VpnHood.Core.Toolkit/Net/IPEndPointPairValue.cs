namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public readonly record struct IPEndPointPairValue(
    IpEndPointValue Source, 
    IpEndPointValue Destination)
{
    public override string ToString() => $"{Source}->{Destination}";
}