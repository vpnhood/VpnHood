using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.Common.Tokens;

namespace VpnHood.AccessServer.Agent.Services;

public class ServerSelectOptions
{
    public required ProjectCache ProjectCache { get; init; }
    public required ServerFarmCache ServerFarmCache { get; init; }
    public required ServerLocationInfo RequestedLocation { get; init; }
    public required bool IncludeIpV6 { get; init; }
    public required string[]? AllowedLocations { get; init; }
    public required string[] ClientTags { get; init; }
    public required bool AllowRedirect { get; init; }
    public required bool IsPremium { get; init; }
}