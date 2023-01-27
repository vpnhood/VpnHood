using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Common.Converters;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Logging;

public static class VhLogger
{
    private static bool _isDiagnoseMode;
    public static ILogger Instance { get; set; } = NullLogger.Instance;
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
        return IsAnonymousMode ? Util.RedactIpAddress(ipAddress) : ipAddress.ToString();
    }

    public static string FormatTypeName(object? obj)
    {
        return obj?.GetType().Name ?? "<null>";
    }

    public static string FormatTypeName<T>()
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

    public static string FormatDns(string dnsName)
    {
        if (IPEndPointConverter.TryParse(dnsName, out var ipEndPoint))
            return Format(ipEndPoint);
        return FormatId(dnsName);
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
}