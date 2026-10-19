using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

public static class VhInvoker
{
    public static async Task TryInvokeAsync(string? actionName, Func<ValueTask> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
        }
    }

    public static async Task TryInvokeAsync(string? actionName, Func<Task> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
        }
    }

    public static async ValueTask<T?> TryInvokeAsync<T>(string? actionName, Func<ValueTask<T>> task,
        T? defaultValue = default)
    {
        try {
            return await task().Vhc();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
            return defaultValue;
        }
    }

    public static async Task<T?> TryInvokeAsync<T>(string? actionName, Func<Task<T>> task, T? defaultValue = default)
    {
        try {
            return await task().Vhc();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
            return defaultValue;
        }
    }

    public static void TryInvoke(Action action)
    {
        TryInvoke((string?)null, action);
    }

    public static void TryInvoke(string? actionName, Action action)
    {
        try {
            action.Invoke();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
        }
    }

    public static T? TryInvoke<T>(Func<T> func, T? defaultValue = default)
        => TryInvoke(null, func, defaultValue);

    public static T? TryInvoke<T>(string? actionName, Func<T> func, T? defaultValue = default)
    {
        try {
            return func();
        }
        catch (Exception ex) {
            LogInvokeError(ex, actionName);
            return defaultValue;
        }
    }

    private static void LogInvokeError(Exception ex, string? actionName)
    {
        if (!string.IsNullOrEmpty(actionName))
            VhLogger.Instance.LogDebug(ex, "Could not invoke {ActionDesc}", actionName);
    }
}
