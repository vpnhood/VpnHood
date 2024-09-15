using System.Net;

namespace VpnHood.NetTester.Utils;

internal static class ArgumentUtils
{
    public static T Get<T>(string[] args, string name)
    {
        // find the index of the argument
        var index = Array.IndexOf(args, name);
        if (index == -1)
            throw new ArgumentException($"Argument {name} is required.");

        var value = index + 1 < args.Length
            ? args[index + 1]
            : throw new ArgumentException($"Argument {name} requires a value.");

        return Parse<T>(value);
    }


    public static T? Get<T>(string[] args, string name, T? defaultValue)
    {
        // find the index of the argument
        var index = Array.IndexOf(args, name);
        if (index == -1)
            return defaultValue;

        var value = index + 1 < args.Length
            ? args[index + 1]
            : throw new ArgumentException($"Argument {name} requires a value.");

        return Parse<T>(value);
    }


    private static T Parse<T>(string value)
    {
        if (typeof(T) == typeof(string))
            return (T)(object)value;

        if (typeof(T) == typeof(bool))
            return (T)(object)bool.Parse(value);

        if (typeof(T) == typeof(int))
            return (T)(object)int.Parse(value);

        if (typeof(T) == typeof(IPAddress))
            return (T)(object)IPAddress.Parse(value);

        if (typeof(T) == typeof(IPEndPoint))
            return (T)(object)IPEndPoint.Parse(value);

        throw new ArgumentException($"Type {typeof(T)} is not supported.");
    }
}