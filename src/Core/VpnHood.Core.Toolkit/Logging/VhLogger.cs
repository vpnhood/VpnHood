using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Logging;

public static class VhLogger
{
    private static readonly VhLoggerDecorator InstanceDecorator = new();

    public static event EventHandler<LoggedEventArgs>? Logged;
    public static ILogger Instance {
        get => InstanceDecorator;
        set {
            // use the decorator to prevent previous assignments losing the instance
            InstanceDecorator.Logger = value is VhLoggerDecorator vhLoggerDecorator ? vhLoggerDecorator.Logger : value;
        }
    }

    public static EventId TcpCloseEventId { get; set; }
    public static bool IsAnonymousMode { get; set; } = true;

    public static LogLevel MinLogLevel { get; set; } = LogLevel.Information;


    public static ILogger CreateConsoleLogger(bool singleLine = false)
    {
        return new VhConsoleLogger(singleLine);
    }

    public static string Format(EndPoint? endPoint)
    {
        if (endPoint == null) return "<null>";
        return endPoint is IPEndPoint ipEndPoint ? Format(ipEndPoint) : endPoint.ToString() ?? "<null>";
    }

    public static string Format(IpEndPointValue? endPoint)
    {
        return Format(endPoint?.ToIPEndPoint());
    }

    public static string Format(IPEndPoint? endPoint)
    {
        if (endPoint == null) return "<null>";

        if (endPoint.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            return $"{Format(endPoint.Address)}:{endPoint.Port}";

        return endPoint.ToString();
    }

    public static string Format(IEnumerable<IPAddress> ipAddresses)
    {
        return string.Join(", ", ipAddresses.Select(Format));
    }

    public static string Format(IPAddress? ipAddress)
    {
        if (ipAddress == null) return "<null>";
        return IsAnonymousMode ? VhUtils.RedactIpAddress(ipAddress) : ipAddress.ToString();
    }

    public static string Format(IEnumerable<IpNetwork> ipNetworks)
    {
        return string.Join(", ", ipNetworks.Select(Format));
    }

    public static string Format(IpNetwork? ipNetwork)
    {
        if (ipNetwork == null) return "<null>";
        return $"{Format(ipNetwork.Prefix)}/{ipNetwork.PrefixLength}";
    }

    public static string FormatType(object? obj)
    {
        return obj?.GetType().Name ?? "<null>";
    }

    public static string FormatType<T>()
    {
        return typeof(T).Name;
    }

    public static string FormatId(object? id)
    {
        if (id == null) return "<null>";

        var str = id.ToString() ?? "";
        return IsAnonymousMode ? "**" + str[(str.Length / 2)..] : str;
    }

    public static string FormatSessionId(object? id)
    {
        return id?.ToString() ?? "<null>";
    }

    public static string FormatHostName(string? dnsName)
    {
        if (dnsName == null) return "<null>";
        if (IPAddress.TryParse(dnsName, out var ipAddress)) return Format(ipAddress);
        if (IPEndPoint.TryParse(dnsName, out var ipEndPoint)) return Format(ipEndPoint);
        return IsAnonymousMode ? VhUtils.RedactHostName(dnsName) : dnsName;
    }

    public static string FormatHostName(string? dnsName, int port)
    {
        return $"{FormatHostName(dnsName)}:{port}";
    }

    public static string FormatIpPacket(string ipPacketText)
    {
        if (!IsAnonymousMode)
            return ipPacketText;

        ipPacketText = RedactIpAddress(ipPacketText, "SourceAddress");
        ipPacketText = RedactIpAddress(ipPacketText, "DestinationAddress");
        ipPacketText = RedactIpAddress(ipPacketText, "Src");
        ipPacketText = RedactIpAddress(ipPacketText, "Dst");
        return ipPacketText;
    }

    private static string RedactIpAddress(string text, string keyText)
    {
        try {
            var start = text.IndexOf($"{keyText}=", StringComparison.Ordinal) + 1;
            if (start == -1)
                return text;
            start += keyText.Length;

            var end = text.IndexOf(",", start, StringComparison.Ordinal);
            var ipAddressText = text[start..end];
            var ipAddress = IPAddress.Parse(ipAddressText);

            text = text[..start] + Format(ipAddress) + text[end..];
            return text;
        }
        catch {
            return "*";
        }
    }

    public static bool IsSocketCloseException(Exception ex)
    {
        return (ex.InnerException != null && IsSocketCloseException(ex.InnerException)) ||
               ex is
                   ObjectDisposedException or
                   OperationCanceledException or
                   TaskCanceledException or
                   SocketException {
                       SocketErrorCode:
                       SocketError.ConnectionAborted or
                       SocketError.OperationAborted or
                       SocketError.ConnectionReset or
                       SocketError.ConnectionRefused or
                       SocketError.NetworkReset
                   };
    }

    public static void LogError(EventId eventId, Exception ex, string message, params object?[] args)
    {
#pragma warning disable CA2254 // it is our log builder, not a simple logging 
        if (IsSocketCloseException(ex)) {
            Instance.LogDebug(TcpCloseEventId, message + $" Message: {ex.Message}", args);
            return;
        }

        Instance.LogError(eventId, ex, message, args);
#pragma warning restore CA2254
    }

    private class VhLoggerDecorator : ILogger
    {
        private readonly AotPreserveHelper _aotPreserveHelper = new();

        // Default is NullLogger — callers that want console output must set
        // VhLogger.Instance = VhLogger.CreateConsoleLogger() explicitly.
        // This prevents LoggerFactory.Create() + Console.Write* from running
        // during class initialization in environments without a readable stdout
        // (e.g. iOS Network Extension).
        public ILogger Logger {
            get => field ??= new VhDeviceLogger();
            set;
        }

        public VhLoggerDecorator()
        {
            // Preserve AOT types. Logger intentionally starts as NullLogger — callers
            // must opt in to console output via VhLogger.Instance = VhLogger.CreateConsoleLogger().
            _ = _aotPreserveHelper.PreserveTypes();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (MinLogLevel > logLevel)
                return;

            // Fire an event
            Logged?.Invoke(null, new LoggedEventArgs(logLevel, eventId, formatter(state, exception), exception));

            Logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel) => Logger.IsEnabled(logLevel);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Logger.BeginScope(state);
    }
}