using System.Diagnostics;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides the means to replace a timeout while a debugger is attached.
/// </summary>
public static class VhUtilsExtensions
{
    /// <summary>
    /// Returns a timeout, or <c>VhUtils.DebuggerTimeout</c> instead while a debugger is attached and it is set, so a
    /// breakpoint does not time a test out.
    /// </summary>
    /// <param name="value">The timeout.</param>
    /// <returns>The timeout to use.</returns>
    public static TimeSpan WhenNoDebugger(this TimeSpan value)
    {
        // read once: set to null between two reads, the second would throw
        return Debugger.IsAttached && VhUtils.DebuggerTimeout is { } debuggerTimeout
            ? debuggerTimeout
            : value;
    }
}