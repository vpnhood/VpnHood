using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Collections;

public static class TimeoutItemUtil
{
    public static void CleanupTimeoutList<T>(List<T> list, TimeSpan timeout) where T : ITimeoutItem
    {
        var now = FastDateTime.Now;
        for (var i = list.Count - 1; i >= 0; i--) {
            var item = list[i];
            if (item.Disposed || now - item.LastUsedTime > timeout) {
                item.Dispose();
                list.RemoveAt(i);
            }
        }
    }
}