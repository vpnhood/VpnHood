namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// The catalog as a fixed list: the ids the build itself declares. This is the right catalog for an
/// app with no backend to ask, and the fallback for one whose backend cannot answer.
/// </summary>
public class StaticAppProductCatalog(IReadOnlyList<string> productIds) : IAppProductCatalog
{
    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return Task.FromResult(productIds);
    }
}
