using System;
using System.Threading.Tasks;

namespace VpnHood.Common.Utils;

public static  class VhTestUtil
{
    public static async Task<bool> WaitForValue<TValue>(object? expectedValue, Func<TValue?> valueFactory, int timeout = 5000)
    {
        const int waitTime = 100;
        for (var elapsed = 0; elapsed < timeout; elapsed += waitTime)
        {
            if (Equals(expectedValue, valueFactory()))
                return true;

            await Task.Delay(waitTime);
        }

        return false;
    }

    public static async Task<bool> WaitForValue<TValue>(object? expectedValue, Func<Task<TValue?>> valueFactory, int timeout = 5000)
    {
        const int waitTime = 100;
        for (var elapsed = 0; elapsed < timeout; elapsed += waitTime)
        {
            if (Equals(expectedValue, await valueFactory()))
                return true;

            await Task.Delay(waitTime);
        }

        return false;
    }

    private static void AssertEquals(object? expected, object? actual, string? message)
    {
        message ??= "Unexpected Value";
        if (!Equals(expected, actual))
            throw new Exception($"{message}. Expected: {expected}, Actual: {actual}");
    }

    public static async Task AssertEqualsWait<TValue>(object? expectedValue, Func<TValue?> valueFactory, 
        string? message = null, int timeout = 5000)
    {
        await WaitForValue(expectedValue, valueFactory, timeout);
        AssertEquals(expectedValue, valueFactory(), message);
    }

    public static async Task AssertEqualsWait<TValue>(object? expectedValue, Func<Task<TValue?>> valueFactory, 
        string? message = null, int timeout = 5000)
    {
        await WaitForValue(expectedValue, valueFactory, timeout);
        AssertEquals(expectedValue, await valueFactory(), message);
    }
}