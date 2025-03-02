using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Logging;

public static class VhLogger
{
    private static bool _isDiagnoseMode;

    private static readonly VhLoggerDecorator InstanceDecorator = new ();

    public static ILogger Instance {
        get => InstanceDecorator;
        set {
            // use the decorator to prevent previous assignments losing the instance
            InstanceDecorator.Logger = value is VhLoggerDecorator vhLoggerDecorator ? vhLoggerDecorator.Logger : value;
        }
    }

    public static EventId TcpCloseEventId { get; set; } = new();
    public static bool IsAnonymousMode { get; set; } = true;

    public static bool IsDiagnoseMode {
        get => _isDiagnoseMode;
        set {
            _isDiagnoseMode = value;
            EventReporter.IsDiagnosticMode = value;
        }
    }

    public static ILogger CreateConsoleLogger(LogLevel logLevel = LogLevel.Information, bool singleLine = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddSimpleConsole(configure => {
                configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                configure.IncludeScopes = true;
                configure.SingleLine = singleLine;
            });
            builder.SetMinimumLevel(logLevel);
        });
        var logger = loggerFactory.CreateLogger("");
        return new SyncLogger(logger);
    }

    public static string Format(EndPoint? endPoint)
    {
        if (endPoint == null) return "<null>";
        return endPoint is IPEndPoint ipEndPoint ? Format(ipEndPoint) : endPoint.ToString();
    }

    public static string Format(IPEndPoint? endPoint)
    {
        if (endPoint == null) return "<null>";

        if (endPoint.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            return $"{Format(endPoint.Address)}:{endPoint.Port}";

        return endPoint.ToString();
    }

    public static string Format(IPAddress? ipAddress)
    {
        if (ipAddress == null) return "<null>";
        return IsAnonymousMode ? VhUtils.RedactIpAddress(ipAddress) : ipAddress.ToString();
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

        var str = id.ToString();
        return IsAnonymousMode ? "**" + str[(str.Length / 2)..] : str;
    }

    public static string FormatSessionId(object? id)
    {
        return id?.ToString() ?? "<null>";
    }

    public static string FormatHostName(string? dnsName)
    {
        if (dnsName == null) return "<null>";
        if (IPEndPointConverter.TryParse(dnsName, out var ipEndPoint))
            return Format(ipEndPoint);

        return IsAnonymousMode ? VhUtils.RedactHostName(dnsName) : dnsName;
    }

    public static string FormatIpPacket(string ipPacketText)
    {
        if (!IsAnonymousMode)
            return ipPacketText;

        ipPacketText = RedactIpAddress(ipPacketText, "SourceAddress");
        ipPacketText = RedactIpAddress(ipPacketText, "DestinationAddress");
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

    // todo: check this
    public static bool IsSocketCloseException(Exception ex)
    {
        return (ex.InnerException != null && IsSocketCloseException(ex.InnerException)) ||
               ex is
                   ObjectDisposedException or
                   OperationCanceledException or
                   TaskCanceledException or
                   SocketException {
                       SocketErrorCode: SocketError.ConnectionAborted or
                       SocketError.OperationAborted or
                       SocketError.ConnectionReset or
                       SocketError.ConnectionRefused or
                       SocketError.NetworkReset
                   };
    }

    public static void LogError(EventId eventId, Exception ex, string message, params object?[] args)
    {
        if (IsSocketCloseException(ex)) {
            Instance.LogDebug(TcpCloseEventId, message + $" Message: {ex.Message}", args);
            return;
        }

        Instance.LogError(eventId, ex, message, args);
    }

    private class VhLoggerDecorator : ILogger
    {
        public ILogger Logger { get; set; } = CreateConsoleLogger();
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Logger.Log(logLevel, eventId, state, exception, formatter);
        public bool IsEnabled(LogLevel logLevel) => Logger.IsEnabled(logLevel);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Logger.BeginScope(state);
    }

}