using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Common.Converters;
using VpnHood.Common.Jobs;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Logging;

public static class VhLogger
{
    private static bool _isDiagnoseMode;
    private static ILogger _instance = NullLogger.Instance;

    public static ILogger Instance
    {
        get => _instance;
        set
        {
            _instance = value;
            JobRunner.Default.Logger = value;
        }
    }

    public static EventId TcpCloseEventId { get; set; } = new();
    public static bool IsAnonymousMode { get; set; } = true;
    public static bool IsDiagnoseMode
    {
        get => _isDiagnoseMode;
        set { _isDiagnoseMode = value; EventReporter.IsDiagnosticMode = value; }
    }

    public static ILogger CreateConsoleLogger(bool verbose = false, bool singleLine = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(configure =>
            {
                configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                configure.IncludeScopes = true;
                configure.SingleLine = singleLine;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
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
        return IsAnonymousMode ? VhUtil.RedactIpAddress(ipAddress) : ipAddress.ToString();
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

        return IsAnonymousMode ? VhUtil.RedactHostName(dnsName) : dnsName;
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
        try
        {
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
        catch
        {
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
            SocketException
            {
                SocketErrorCode: SocketError.ConnectionAborted or 
                SocketError.OperationAborted or
                SocketError.ConnectionReset or
                SocketError.ConnectionRefused or
                SocketError.NetworkReset
            };
    }

    public static void LogError(EventId eventId, Exception ex, string message, params object?[] args)
    {
        if (IsSocketCloseException(ex))
        {
            Instance.LogTrace(TcpCloseEventId, message + $" Message: {ex.Message}", args);
            return;
        }

        Instance.LogError(eventId, ex, message, args);
    }

}