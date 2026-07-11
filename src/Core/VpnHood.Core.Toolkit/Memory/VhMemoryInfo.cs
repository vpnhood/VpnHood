namespace VpnHood.Core.Toolkit.Memory;

/// <summary>
/// Cross-platform snapshot of process/system memory figures. Every field is nullable on purpose: no single OS
/// exposes all of them, so each host fills what it can measure and leaves the rest <c>null</c>. Bytes are the
/// canonical unit; the <c>*Mb</c> helpers are convenience conversions.
/// </summary>
public readonly struct VhMemoryInfo
{
    private const double Mib = 1024.0 * 1024.0;

    /// <summary>
    /// This process's memory footprint in bytes — the figure an OS memory limit is enforced against (e.g. iOS
    /// jetsam's <c>phys_footprint</c>). <c>null</c> when the platform can't report it.
    /// </summary>
    public long? ProcessFootprintBytes { get; init; }

    /// <summary>Total physical system memory (or container/cgroup limit) in bytes. <c>null</c> when unavailable.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>System memory currently in use in bytes. <c>null</c> when unavailable.</summary>
    public long? UsedBytes { get; init; }

    public double? ProcessFootprintMb => ToMb(ProcessFootprintBytes);
    public double? TotalMb => ToMb(TotalBytes);
    public double? UsedMb => ToMb(UsedBytes);

    private static double? ToMb(long? bytes) => bytes / Mib;
}
