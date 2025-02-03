using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.Abstractions;

public class ShowAdResult
{
    public required string? AdData { get; init; }
    public required string? NetworkName { get; init; }
    public required ApiError? ApiError { get; init; }

}