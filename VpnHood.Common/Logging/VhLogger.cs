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

        public static bool IsAnonymousMode { get; set; } = true;
        public static bool IsDiagnoseMode { get; set; } = false;

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
            var addressBytes = ipAddress.GetAddressBytes();

            if (IsAnonymousMode && ipAddress.AddressFamily == AddressFamily.InterNetwork &&
                !ipAddress.Equals(IPAddress.Any) &&
                !ipAddress.Equals(IPAddress.Loopback))
                return $"{addressBytes[0]}.*.*.{addressBytes[3]}";

            if (IsAnonymousMode && ipAddress.AddressFamily == AddressFamily.InterNetworkV6 &&
                !ipAddress.Equals(IPAddress.IPv6Any) &&
                !ipAddress.Equals(IPAddress.IPv6Loopback))
                return $"{addressBytes[0]:x2}{addressBytes[1]:x2}:***:{addressBytes[14]:x2}{addressBytes[15]:x2}";

            return ipAddress.ToString();
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
            var start = text.IndexOf($"{keyText}=") + 1;
            if (start == -1)
                return text;
            start += keyText.Length;

            var end = text.IndexOf(",", start);
            var ipAddressText = text[start..end];
            var ipAddress = IPAddress.Parse(ipAddressText);
            text = text.Replace(ipAddressText, Format(ipAddress));
            return text;
        }
    }
}