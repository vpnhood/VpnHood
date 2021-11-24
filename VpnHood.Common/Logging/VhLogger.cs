using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Common.Converters;

namespace VpnHood.Common.Logging
{
    public static class VhLogger
    {
        public static ILogger Instance { get; set; } = NullLogger.Instance;

        public static bool IsAnonymousMode { get; set; } = false;
        public static bool IsDiagnoseMode { get; set; } = false;

        public static ILogger CreateConsoleLogger(bool verbose = false, bool singleLine = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole(configure =>
                {
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
            return endPoint is IPEndPoint point ? Format(point) : endPoint.ToString();
        }

        public static string Format(IPEndPoint? endPoint)
        {
            if (endPoint == null) return "<null>";

            if (endPoint.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                return $"{Format(endPoint.Address)}:{endPoint.Port}";

            return endPoint.ToString();
        }

        public static string Format(IPAddress? iPAddress)
        {
            if (iPAddress == null) return "<null>";
            var addressBytes = iPAddress.GetAddressBytes();

            if (IsAnonymousMode && iPAddress.AddressFamily == AddressFamily.InterNetwork)
                return $"{addressBytes[0]}.*.*.{addressBytes[3]}";

            if (IsAnonymousMode && iPAddress.AddressFamily == AddressFamily.InterNetworkV6)
                return $"{iPAddress.GetAddressBytes()[0]}.{iPAddress.GetAddressBytes()[1]}.*.{iPAddress.GetAddressBytes()[3]}";

            return iPAddress.ToString();
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
            var str = id?.ToString();
            return str == null ? "<null>" : str[..Math.Min(5, str.Length)] + "**";
        }

        public static string FormatSessionId(uint id)
        {
            return id.ToString();
        }

        public static string FormatDns(string dnsName)
        {
            if (IPEndPointConverter.TryParse(dnsName, out var ipEndPoint))
                return Format(ipEndPoint);
            return FormatId(dnsName);
        }
    }
}