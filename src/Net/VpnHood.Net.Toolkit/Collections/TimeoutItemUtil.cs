using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Collections;

/// <summary>
/// Provides the means to clean a list of timeout items.
/// </summary>
public static class TimeoutItemUtil
{
    /// <summary>
    /// Removes from a list, and disposes, the items that are disposed or unused for longer than a timeout.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="list">The list; it is not safe for concurrent use.</param>
    /// <param name="timeout">How long an item may go unused.</param>
    public static void CleanupTimeoutList<T>(List<T> list, TimeSpan timeout) where T : ITimeoutItem
    {
        var now = FastDateTime.UtcNow;
        for (var i = list.Count - 1; i >= 0; i--) {
            var item = list[i];
            if (item.IsDisposed || now - item.LastUsedTime > timeout) {
                item.Dispose();
                list.RemoveAt(i);
            }
        }
    }
}