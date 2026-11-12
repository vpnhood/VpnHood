namespace VpnHood.Core.Server.Abstractions;

/// <summary>Represents the server machine's swap memory, in bytes.</summary>
public class SwapMemoryInfo
{
    /// <summary>Gets the size of all the swap memory, the server's own file with it.</summary>
    public required long TotalSize { get; init; }

    /// <summary>Gets the swap memory in use, in all.</summary>
    public required long TotalUsed { get; init; }

    /// <summary>Gets the size of the server's own swap file.</summary>
    public required long AppSize { get; init; }

    /// <summary>Gets the part of the server's own swap file in use.</summary>
    public required long AppUsed { get; init; }
}