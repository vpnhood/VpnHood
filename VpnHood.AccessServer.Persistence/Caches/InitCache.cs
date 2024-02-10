namespace VpnHood.AccessServer.Persistence.Caches;

public class InitCache
{
    public required ServerCache[] Servers { get; init; }
    public required ServerFarmCache[] Farms { get; init; }
    public required ProjectCache[] Projects { get; init; }
    public required SessionCache[] Sessions { get; init; }
    public required AccessCache[] Accesses { get; init; }
}