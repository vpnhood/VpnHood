using System.Diagnostics;

namespace VpnHood.Net.Toolkit.Utils;

public static class VhUtilsExtensions
{
    public static TimeSpan WhenNoDebugger(this TimeSpan value)
    {
        return Debugger.IsAttached && VhUtils.DebuggerTimeout != null
            ? VhUtils.DebuggerTimeout.Value
            : value;
    }
}