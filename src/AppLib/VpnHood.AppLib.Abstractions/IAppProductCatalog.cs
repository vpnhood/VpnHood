namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Which store products this build may sell. Neither StoreKit nor Play Billing can list an app's
/// own catalog — both answer only for the ids handed to them — so the ids must come from somewhere,
/// and this is that somewhere. Keeping it behind an interface is what lets a backend own the list
/// instead of the binary: adding a plan then costs a config change, not a store release.
/// </summary>
public interface IAppProductCatalog
{
    Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken);
}
