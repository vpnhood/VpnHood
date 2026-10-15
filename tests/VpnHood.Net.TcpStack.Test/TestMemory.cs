using VpnHood.Net.Toolkit.Memory;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>A memory reader whose process footprint the test sets.</summary>
internal sealed class TestMemory : VhMemory
{
    public double FootprintMb { get; set; }

    public override VhMemoryInfo GetInfo() =>
        new() { ProcessFootprintBytes = (long)(FootprintMb * 1024 * 1024) };
}
