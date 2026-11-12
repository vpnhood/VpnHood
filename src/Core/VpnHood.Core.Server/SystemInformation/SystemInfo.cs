using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.SystemInformation;

/// <summary>Represents a server machine's information: its OS, memory and processors.</summary>
public class SystemInfo
{
    /// <summary>Gets the OS's description, as the provider gives it.</summary>
    public required string OsInfo { get; init; }

    /// <summary>
    /// Gets the machine's memory in all, in bytes, as the provider reads it, or <see langword="null"/> where it reads
    /// none; a provider may give 0 where its reading failed.
    /// </summary>
    public required long? TotalMemory { get; init; }

    /// <summary>
    /// Gets the memory available now, in bytes, as the provider reads it, or <see langword="null"/> where it reads none;
    /// a provider may give 0 where its reading failed.
    /// </summary>
    public required long? AvailableMemory { get; init; }

    /// <summary>
    /// Gets the processors' use, in percent, as the provider samples it, or <see langword="null"/> where it samples none;
    /// a provider may give 0 where its reading failed.
    /// </summary>
    public required int? CpuUsage { get; init; }

    /// <summary>Gets the number of logical processors.</summary>
    public required int LogicalCoreCount { get; init; }

    /// <inheritdoc/>
    /// <remarks>It is the OS, the memory in all and the logical processors.</remarks>
    public override string ToString()
    {
        var totalMemory = TotalMemory != null ? VhUtils.FormatBytes(TotalMemory.Value) : "*";
        return $"{OsInfo}, TotalMemory: {totalMemory}, Logical Cores: {LogicalCoreCount}";
    }
}