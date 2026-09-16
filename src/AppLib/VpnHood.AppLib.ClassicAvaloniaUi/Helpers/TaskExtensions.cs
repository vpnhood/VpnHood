using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

internal static class TaskExtensions
{
    // A task nothing waits for - a flag saved as a page opens - whose failure must still be seen:
    // the log gets it, with what was being done.
    public static void Forget(this Task task, string action)
    {
        task.ContinueWith(
            t => VhLogger.Instance.LogError(t.Exception, "{Action}", action),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
