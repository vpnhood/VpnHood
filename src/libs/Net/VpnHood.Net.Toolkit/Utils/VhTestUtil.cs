using System.Diagnostics;
using System.Net;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides test assertions that wait for a value, or check the exception an API call throws.
/// </summary>
/// <remarks>
/// A wait reads the value every 100 milliseconds until it matches or the timeout passes; a read under way is not cut by
/// the timeout, as it may still bring the value. The caller's token ends the wait for an asynchronous read, not a
/// synchronous factory, nor the task itself.
/// </remarks>
public static class VhTestUtil
{
    /// <summary>The exception that is thrown when an assertion fails.</summary>
    /// <param name="message">The message, or <see langword="null"/>.</param>
    /// <param name="innerException">The exception that is the cause of this one, or <see langword="null"/>.</param>
    public class AssertException(string? message = null, Exception? innerException = null) :
        Exception(message, innerException);

    private static async Task<TValue> WaitForValue<TValue>(TValue expectedValue, Func<TValue> valueFactory,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        const int waitTime = 100;
        CancellationTokenSource cancellationTokenSource = new(timeout);
        var actualValue = valueFactory();
        while (!cancellationTokenSource.IsCancellationRequested) {
            if (Equals(expectedValue, actualValue))
                return actualValue;

            await Task.Delay(waitTime, cancellationToken).Vhc();
            actualValue = valueFactory();
        }

        return actualValue;
    }

    private static async Task<TValue> WaitForValue<TValue>(TValue expectedValue, 
        Func<Task<TValue>> valueFactory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        const int waitTime = 100;
        CancellationTokenSource cancellationTokenSource = new(timeout);
        var actualValue = await valueFactory().WaitAsync(cancellationToken).Vhc();
        while (!cancellationTokenSource.IsCancellationRequested) {
            if (Equals(expectedValue, actualValue))
                return actualValue;

            await Task.Delay(waitTime, cancellationToken).Vhc();
            actualValue = await valueFactory().WaitAsync(cancellationToken).Vhc();
        }

        return actualValue;
    }


    private static void AssertEquals(object? expected, object? actual, string? message)
    {
        message ??= "Unexpected Value";
        if (!Equals(expected, actual))
            throw new AssertException($"{message}. Expected: {expected}, Actual: {actual}");
    }

    /// <summary>Waits until a value matches, and fails when it does not by the timeout.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="valueFactory">Reads the value.</param>
    /// <param name="message">The failure's message, or <see langword="null"/> for <c>Unexpected Value</c>.</param>
    /// <param name="timeout">How long to wait, in milliseconds. The default is 5000.</param>
    /// <param name="noTimeoutOnDebugger"><see langword="true"/> to wait a day while a debugger is attached.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the value matches.</returns>
    /// <exception cref="AssertException">The value still differs at the timeout.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, other than -1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Func<TValue> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true, 
        CancellationToken cancellationToken = default)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, valueFactory, timeoutSpan, cancellationToken).Vhc();
        AssertEquals(expectedValue, actualValue, message);
    }

    /// <summary>Waits until a value read asynchronously matches, and fails when it does not by the timeout.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="valueFactory">Reads the value.</param>
    /// <param name="message">The failure's message, or <see langword="null"/> for <c>Unexpected Value</c>.</param>
    /// <param name="timeout">How long to wait, in milliseconds. The default is 5000.</param>
    /// <param name="noTimeoutOnDebugger"><see langword="true"/> to wait a day while a debugger is attached.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the value matches.</returns>
    /// <exception cref="AssertException">The value still differs at the timeout.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, other than -1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Func<Task<TValue>> valueFactory,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true, 
        CancellationToken cancellationToken = default)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, valueFactory, timeoutSpan, cancellationToken).Vhc();
        AssertEquals(expectedValue, actualValue, message);
    }

    /// <summary>Waits for a task, and fails when its result does not match.</summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="task">The task.</param>
    /// <param name="message">The failure's message, or <see langword="null"/> for <c>Unexpected Value</c>.</param>
    /// <param name="timeout">How long to wait, in milliseconds. The default is 5000.</param>
    /// <param name="noTimeoutOnDebugger"><see langword="true"/> to wait a day while a debugger is attached.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the value matches.</returns>
    /// <exception cref="AssertException">The value still differs at the timeout.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, other than -1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task AssertEqualsWait<TValue>(TValue expectedValue, Task<TValue> task,
        string? message = null, int timeout = 5000, bool noTimeoutOnDebugger = true, 
        CancellationToken cancellationToken = default)
    {
        var timeoutSpan = noTimeoutOnDebugger && Debugger.IsAttached
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromMilliseconds(timeout);

        var actualValue = await WaitForValue(expectedValue, () => task, timeoutSpan, cancellationToken).Vhc();
        AssertEquals(expectedValue, actualValue, message);
    }

    /// <summary>Awaits a task that must throw an <see cref="ApiException"/> with a status code.</summary>
    /// <param name="expectedStatusCode">The status code.</param>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded.</exception>
    /// <exception cref="Exception">The task threw with another status code.</exception>
    public static Task<Exception> AssertApiException(HttpStatusCode expectedStatusCode, Task task,
        string? message = null)
    {
        return AssertApiException((int)expectedStatusCode, task, message);
    }

    private static void AssertExceptionContains(Exception ex, string? contains)
    {
        if (contains != null && !ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Actual error message does not contain \"{contains}\".");
    }

    /// <summary>Awaits a task that must throw an <see cref="ApiException"/> with a status code.</summary>
    /// <param name="expectedStatusCode">The status code.</param>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <param name="contains">Text the exception's message must hold, ignoring case, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded.</exception>
    /// <exception cref="Exception">
    /// The task threw with another status code or message, or threw another exception, which passes through.
    /// </exception>
    public static async Task<Exception> AssertApiException(int expectedStatusCode, Task task,
        string? message = null, string? contains = null)
    {
        try {
            await task.Vhc();
            throw new AssertException($"Expected {expectedStatusCode} but the actual was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.StatusCode != expectedStatusCode)
                throw new Exception($"Expected {expectedStatusCode} but the actual was {ex.StatusCode}. {message}");

            AssertExceptionContains(ex, contains);
            return ex;
        }
    }

    /// <summary>
    /// Awaits a task that must throw an exception of a type, or an <see cref="ApiException"/> that names it; the type's
    /// simple name is compared, not its identity.
    /// </summary>
    /// <typeparam name="T">The exception's type.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <param name="contains">Text the exception's message must hold, ignoring case, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded, or threw another type.</exception>
    /// <exception cref="Exception">The exception's message does not hold the text.</exception>
    public static Task<Exception> AssertApiException<T>(Task task, string? message = null, string? contains = null)
    {
        return AssertApiException(typeof(T).Name, task, message, contains);
    }

    /// <summary>
    /// Awaits a task that must throw an exception of a type's name, or an <see cref="ApiException"/> that names it.
    /// </summary>
    /// <param name="expectedExceptionType">The exception type's name.</param>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <param name="contains">Text the exception's message must hold, ignoring case, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded, or threw another type.</exception>
    /// <exception cref="Exception">The exception's message does not hold the text.</exception>
    public static async Task<Exception> AssertApiException(string expectedExceptionType, Task task,
        string? message = null, string? contains = null)
    {
        try {
            await task.Vhc();
            throw new AssertException($"Expected {expectedExceptionType} exception but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != expectedExceptionType)
                throw new AssertException(
                    $"Expected {expectedExceptionType} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
            return ex;
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (ex.GetType().Name != expectedExceptionType)
                throw new AssertException($"Expected {expectedExceptionType} but was {ex.GetType().Name}. {message}",
                    ex);

            AssertExceptionContains(ex, contains);
            return ex;
        }
    }

    /// <summary>
    /// Awaits a task that must throw what tells an object was not found (<see cref="NotExistsException.Is"/>), or an
    /// <see cref="ApiException"/> that names <see cref="NotExistsException"/>.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <param name="contains">Text the exception's message must hold, ignoring case, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded, or threw another kind.</exception>
    /// <exception cref="Exception">The exception's message does not hold the text.</exception>
    public static async Task<Exception> AssertNotExistsException(Task task, string? message = null,
        string? contains = null)
    {
        try {
            await task.Vhc();
            throw new AssertException($"Expected kind of {nameof(NotExistsException)} but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != nameof(NotExistsException))
                throw new AssertException(
                    $"Expected {nameof(NotExistsException)} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
            return ex;
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (!NotExistsException.Is(ex))
                throw new AssertException(
                    $"Expected kind of {nameof(NotExistsException)} but was {ex.GetType().Name}. {message}", ex);

            AssertExceptionContains(ex, contains);
            return ex;
        }
    }

    /// <summary>
    /// Awaits a task that must throw what tells an object exists already (<see cref="AlreadyExistsException.Is"/>), or an
    /// <see cref="ApiException"/> that names <see cref="AlreadyExistsException"/>.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="message">Added to the failure's message, or <see langword="null"/>.</param>
    /// <param name="contains">Text the exception's message must hold, ignoring case, or <see langword="null"/>.</param>
    /// <returns>A task whose result is the exception.</returns>
    /// <exception cref="AssertException">The task succeeded, or threw another kind.</exception>
    /// <exception cref="Exception">The exception's message does not hold the text.</exception>
    public static async Task<Exception> AssertAlreadyExistsException(Task task, string? message = null,
        string? contains = null)
    {
        try {
            await task.Vhc();
            throw new AssertException($"Expected kind of {nameof(AlreadyExistsException)} but was OK. {message}");
        }
        catch (ApiException ex) {
            if (ex.ExceptionTypeName != nameof(AlreadyExistsException))
                throw new AssertException(
                    $"Expected {nameof(AlreadyExistsException)} but was {ex.ExceptionTypeName}. {message}");

            AssertExceptionContains(ex, contains);
            return ex;
        }
        catch (Exception ex) when (ex is not AssertException) {
            if (!AlreadyExistsException.Is(ex))
                throw new AssertException(
                    $"Expected kind of {nameof(AlreadyExistsException)} but was {ex.GetType().Name}. {message}", ex);

            AssertExceptionContains(ex, contains);
            return ex;
        }
    }
}