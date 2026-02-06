namespace VpnHood.Core.DomainFiltering.Tls;

public class TlsSniData
{
    public required string? Sni { get; init; }
    public required Memory<byte> ReadData { get; init; }
}