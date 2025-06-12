using System.Diagnostics;
using System.Net;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.Core.Toolkit.Utils;

public static class VhTestUtil
{
    public class AssertException : Exception
    {
        public AssertException(string? message = null)
            : base(message)
        {
        }

        public AssertException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private static async Task<TValue> WaitForValue<TValue>(TValue expectedValue, Func<TValue> valueFactory, TimeSpan timeout)
    {
        const int waitTime = 100;
        CancellationTokenSource cancellationTokenSource = new(timeout);
        var actualValue = valueFactory();
        while (!cancellationTokenSource.IsCancellationRequested) {
            if (Equals(expectedValue, actualValue))
                return actualValue;

            await Task.Delay(waitTime, CancellationToken.None);
            actualValue = valueFactory();
        }

        return actualValue;
    }

    private static async Task<TValue> WaitForValue<TValue>(TValue expectedValue, Func<Task<TValue>> valueFactory, TimeSpan timeout)
    {
        const int waitTime = 100;
        CancellationTokenSource cancellationTokenSource = new(timeout);
        var actualValue = await valueFactory();
        while (!cancellationTokenSource.IsCancellationRequested) {
            if (Equals(expectedValue, actualValue))
                return actualValue;

            await Task.Delay(waitTime, CancellationToken.None);
            actualValue = await valueFactory();
        }

        return actualValue;
    }


    private static void AssertEquals(object? expected, object? actual, string? message)
    {
        message ??= "Unexpected Value";
        if (!Equals(expected, actual))
            throw new AssertException($"{message}. Expected: {expected}, Actual: {actual}");
    }

    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Func<TValue> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached 
            ? VhUtils.DebuggerTimeout
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, valueFactory, timeoutSpan);
        AssertEquals(expectedValue, actualValue, message);
    }

    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Func<Task<TValue>> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached
            ? VhUtils.DebuggerTimeout
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, valueFactory, timeoutSpan);
        AssertEquals(expectedValue, actualValue, message);
    }

    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Task<TValue> task,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached
            ? VhUtils.DebuggerTimeout
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, () => task, timeoutSpan);
        AssertEquals(expectedValue, actualValue, message);
    }

    public static Task AssertApiException(HttpStatusCode expectedStatusCode, Task task, string? message = null)
    {
        return AssertApiException((int)expectedStatusCode, task, message);
    }

    private static void AssertExceptionContains(Exception ex, string? contains)
    {
        if (contains != null && !ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Actual error message does not contain \"{contains}\".");
    }

    public static async Task AssertApiException(int expectedStatusCode, Task task,
        string? message = null, string? contains = null)
    {
        try {
            await task;
            throw new AssertException($"Expected {expectedStatusCode} but the actual was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.StatusCode != expectedStatusCode)
                throw new Exception($"Expected {expectedStatusCode} but the actual was {ex.StatusCode}. {message}");

            AssertExceptionContains(ex, contains);
        }
    }

    public static Task AssertApiException<T>(Task task, string? message = null, string? contains = null)
    {
        return AssertApiException(typeof(T).Name, task, message, contains);
    }

    public static async Task AssertApiException(string expectedExceptionType, Task task,
        string? message = null, string? contains = null)
    {
        try {
            await task;
            throw new AssertException($"Expected {expectedExceptionType} exception but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != expectedExceptionType)
                throw new AssertException(
                    $"Expected {expectedExceptionType} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (ex.GetType().Name != expectedExceptionType)
                throw new AssertException($"Expected {expectedExceptionType} but was {ex.GetType().Name}. {message}",
                    ex);

            AssertExceptionContains(ex, contains);
        }
    }

    public static async Task AssertNotExistsException(Task task, string? message = null, string? contains = null)
    {
        try {
            await task;
            throw new AssertException($"Expected kind of {nameof(NotExistsException)} but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != nameof(NotExistsException))
                throw new AssertException(
                    $"Expected {nameof(NotExistsException)} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (!NotExistsException.Is(ex))
                throw new AssertException(
                    $"Expected kind of {nameof(NotExistsException)} but was {ex.GetType().Name}. {message}", ex);

            AssertExceptionContains(ex, contains);
        }
    }

    public static async Task AssertAlreadyExistsException(Task task, string? message = null, string? contains = null)
    {
        try {
            await task;
            throw new AssertException($"Expected kind of {nameof(AlreadyExistsException)} but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != nameof(AlreadyExistsException))
                throw new AssertException(
                    $"Expected {nameof(AlreadyExistsException)} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (!AlreadyExistsException.Is(ex))
                throw new AssertException(
                    $"Expected kind of {nameof(AlreadyExistsException)} but was {ex.GetType().Name}. {message}", ex);

            AssertExceptionContains(ex, contains);
        }
    }
}