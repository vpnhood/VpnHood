using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

// Runs what may fail without letting the failure out: it is logged as "Could not {actionName}." at the level the
// caller gives - Debug for best-effort work, Warning for what a failure leaves behind, Error for a bug such as a
// failed handler. A cancellation is no failure, and goes at Debug.
public static class VhInvoker
{
    public static void TryInvoke(string actionName, LogLevel failureLevel, Action action)
    {
        try {
            action();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    public static T? TryInvoke<T>(string actionName, LogLevel failureLevel, Func<T> func, T? defaultValue = default)
    {
        try {
            return func();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
            return defaultValue;
        }
    }

    public static async Task TryInvokeAsync(string actionName, LogLevel failureLevel, Func<Task> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    public static async Task TryInvokeAsync(string actionName, LogLevel failureLevel, Func<ValueTask> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    public static async Task<T?> TryInvokeAsync<T>(string actionName, LogLevel failureLevel, Func<Task<T>> task,
        T? defaultValue = default)
    {
        try {
            return await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
            return defaultValue;
        }
    }

    public static async ValueTask<T?> TryInvokeAsync<T>(string actionName, LogLevel failureLevel,
        Func<ValueTask<T>> task, T? defaultValue = default)
    {
        try {
            return await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
            return defaultValue;
        }
    }

    // the one wording of a swallowed failure, the dispose and cancel helpers' too
    internal static void LogFailure(Exception ex, string actionName, LogLevel failureLevel)
    {
        if (ex is OperationCanceledException)
            VhLogger.Instance.LogDebug(ex, "Could not {ActionName}, as it was cancelled.", actionName);
        else
            VhLogger.Instance.Log(failureLevel, ex, "Could not {ActionName}.", actionName);
    }
}
