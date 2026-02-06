namespace VpnHood.Core.DomainFiltering.SniExtractors;

public interface IPacketSniResult
{
    string? DomainName { get; init; }
    bool NeedMore { get; init; }
}