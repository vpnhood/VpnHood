using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides the means to run what may fail without letting the failure out: it is logged as
/// <c>Could not {actionName}.</c> at the level the caller gives.
/// </summary>
/// <remarks>
/// Give Debug for best-effort work, Warning for what a failure leaves behind, and Error for a bug, such as a failed
/// handler. Every exception is caught; a cancellation is logged at Debug with its own wording. A failure of the log itself
/// is not caught.
/// </remarks>
public static class VhInvoker
{
    /// <summary>Runs an action, and logs a failure instead of throwing it.</summary>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="action">The action.</param>
    public static void TryInvoke(string actionName, LogLevel failureLevel, Action action)
    {
        try {
            action();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    /// <summary>Runs a function, and logs a failure instead of throwing it.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="func">The function.</param>
    /// <param name="defaultValue">The result after a failure.</param>
    /// <returns>The function's result, or <paramref name="defaultValue"/> after a failure.</returns>
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

    /// <summary>Runs an asynchronous action, and logs a failure instead of throwing it.</summary>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="task">Starts the action.</param>
    /// <returns>A task that completes when the action does, failed or not.</returns>
    public static async Task TryInvokeAsync(string actionName, LogLevel failureLevel, Func<Task> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    /// <summary>Runs an asynchronous action, and logs a failure instead of throwing it.</summary>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="task">Starts the action.</param>
    /// <returns>A task that completes when the action does, failed or not.</returns>
    public static async Task TryInvokeAsync(string actionName, LogLevel failureLevel, Func<ValueTask> task)
    {
        try {
            await task().Vhc();
        }
        catch (Exception ex) {
            LogFailure(ex, actionName, failureLevel);
        }
    }

    /// <summary>Runs an asynchronous function, and logs a failure instead of throwing it.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="task">Starts the function.</param>
    /// <param name="defaultValue">The result after a failure.</param>
    /// <returns>A task whose result is the function's, or <paramref name="defaultValue"/> after a failure.</returns>
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

    /// <summary>Runs an asynchronous function, and logs a failure instead of throwing it.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="actionName">What is being done, for the log line <c>Could not {actionName}.</c></param>
    /// <param name="failureLevel">The level a failure is logged at; a cancellation is logged at Debug.</param>
    /// <param name="task">Starts the function.</param>
    /// <param name="defaultValue">The result after a failure.</param>
    /// <returns>A task whose result is the function's, or <paramref name="defaultValue"/> after a failure.</returns>
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
