namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>Selects which endpoint categories DeleteAll removes. Defaults delete everything.</summary>
public class DeleteAllOptions
{
    public bool DeleteSucceeded { get; init; } = true;
    public bool DeleteFailed { get; init; } = true;
    public bool DeleteUnknown { get; init; } = true;
    public bool DeleteDisabled { get; init; } = true;
}
