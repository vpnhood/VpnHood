using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.ClientFilters;

public class ClientFilterUpdateParams
{
    public Patch<string>? ClientFilterName { get; init; }
    public Patch<string>? Filter { get; init; }
}