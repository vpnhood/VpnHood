using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net;
using VpnHood.Common;

namespace VpnHood.Logging
{
    public static class VhLogger
    {
        public static ILogger Current { get; set; } = NullLogger.Instance;
        public static ILogger CreateConsoleLogger(bool verbose = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole((configure) => { configure.IncludeScopes = true; configure.SingleLine = false; });
                builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);

            });
            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }

        public static bool IsAnonymousMode { get; set; } = false;
        public static bool IsDiagnoseMode { get; set; } = false;

        public static string Format(EndPoint endPoint)
        {
            if (endPoint == null) return "<null>";
            return endPoint is IPEndPoint point ? Format(point) : endPoint.ToString();
        }

        public static string Format(IPEndPoint endPoint)
        {
            if (endPoint == null) return "<null>";

            if (IsAnonymousMode && endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return $"{Format(endPoint.Address)}:{endPoint.Port}";
            else
                return endPoint.ToString();
        }

        public static string Format(IPAddress iPAddress)
        {
            if (iPAddress == null) return "<null>";

            if (IsAnonymousMode && iPAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return $"{iPAddress.GetAddressBytes()[0]}.*.*.{iPAddress.GetAddressBytes()[3]}";
            else
                return iPAddress.ToString();
        }

        public static string FormatTypeName(object obj) => obj?.GetType().Name ?? "<null>";

        public static string FormatTypeName<T>() => typeof(T).Name;

        public static string FormatId(object id)
        {
            var str = id.ToString();
            return id == null ? "<null>" : str.Substring(0, Math.Min(5, str.Length)) + "**";
        }

        public static string FormatDns(string dnsName)
        {
            if (Util.TryParseIpEndPoint(dnsName, out IPEndPoint ipEndPoint))
                return Format(ipEndPoint);
            return FormatId(dnsName);
        }
    }
}
