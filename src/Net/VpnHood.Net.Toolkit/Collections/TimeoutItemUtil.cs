using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Collections;

public static class TimeoutItemUtil
{
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