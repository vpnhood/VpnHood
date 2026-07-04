namespace VpnHood.Core.Toolkit.Memory;

/// <summary>
/// Ambient context for reading memory figures (see <see cref="VhMemoryInfo"/>) — including the process
/// footprint an OS memory limit is enforced against (e.g. iOS jetsam's <c>phys_footprint</c>). .NET has no
/// single cross-platform API for these, so each host installs a platform implementation once at init by
/// assigning <see cref="Instance"/> (mirrors how <c>VhLogger.Instance</c> is set). Whatever a platform can't
/// measure it returns as <c>null</c>. Consumers read via <c>VhMemory.Instance</c> without any dependency on a
/// specific host; this type lives in Toolkit because every assembly already references it.
/// <para>
/// <see cref="Instance"/> defaults to a null-object that reports nothing, so callers never need a null check.
/// </para>
/// </summary>
public abstract class VhMemory
{
    /// <summary>
    /// The active implementation. Defaults to a no-op that reports all-<c>null</c>; a host replaces it at init
    /// (e.g. the iOS device/service installs its native reader). Set once during startup.
    /// </summary>
    public static VhMemory Instance { get; set; } = new NullVhMemory();

    /// <summary>Returns the current memory snapshot. Implementations must not throw (report unavailable
    /// figures as <c>null</c> instead), because consumers read this on hot paths. Read a specific figure via
    /// the returned struct, e.g. <c>VhMemory.Instance.GetInfo().ProcessFootprintMb</c>.</summary>
    public abstract VhMemoryInfo GetInfo();

    /// <summary>Default null-object: reports no memory figures. Used until a host installs a real one.</summary>
    private sealed class NullVhMemory : VhMemory
    {
        public override VhMemoryInfo GetInfo() => default;
    }
}
