namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Selects the endpoints <see cref="IProxyEndPointStore.DeleteAll"/> deletes. The defaults delete them all, but an
/// enabled endpoint whose last success and last failure are at the same moment, which is in no group.
/// </summary>
/// <remarks>
/// Each option deletes its endpoints whatever the others say: a disabled endpoint whose last use succeeded goes with
/// <see cref="DeleteSucceeded"/>, even when <see cref="DeleteDisabled"/> is <see langword="false"/>.
/// </remarks>
public class DeleteAllOptions
{
    /// <summary>Gets a value indicating whether endpoints whose last use succeeded are deleted.</summary>
    public bool DeleteSucceeded { get; init; } = true;

    /// <summary>Gets a value indicating whether endpoints whose last use failed are deleted.</summary>
    public bool DeleteFailed { get; init; } = true;

    /// <summary>Gets a value indicating whether endpoints never used are deleted.</summary>
    public bool DeleteUnknown { get; init; } = true;

    /// <summary>Gets a value indicating whether disabled endpoints are deleted.</summary>
    public bool DeleteDisabled { get; init; } = true;
}
