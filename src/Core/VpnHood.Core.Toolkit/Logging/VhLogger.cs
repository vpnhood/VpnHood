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

    // kept here because every caller reaches redaction through VhLogger; Redactor owns the state, and the
    // mode is fixed per instance, so changing it swaps the default redactor rather than mutating one
    public static bool IsAnonymousMode {
        get => Redactor.Default.IsAnonymousMode;
        set {
            if (Redactor.Default.IsAnonymousMode != value)
                Redactor.Default = new Redactor(value);
        }
    }

    public static LogLevel MinLogLevel { get; set; } = LogLevel.Information;


    public static ILogger CreateConsoleLogger(bool singleLine = false)
    {
        return new VhConsoleLogger(singleLine);
    }

    public static Redactor.RedactedValue<EndPoint> Format(EndPoint? endPoint)
    {
        return Redactor.Default.Format(endPoint);
    }

    public static Redactor.RedactedValue<EndPoint> Format(IPEndPoint? endPoint)
    {
        return Redactor.Default.Format(endPoint);
    }

    public static Redactor.RedactedValue<EndPoint> Format(IpEndPointValue? endPoint)
    {
        return Redactor.Default.Format(endPoint);
    }

    public static Redactor.RedactedValue<IPAddress> Format(IPAddress? ipAddress)
    {
        return Redactor.Default.Format(ipAddress);
    }

    public static Redactor.RedactedValue<IpNetwork> Format(IpNetwork? ipNetwork)
    {
        return Redactor.Default.Format(ipNetwork);
    }

    public static Redactor.RedactedValue<IReadOnlyList<IPAddress>> Format(IEnumerable<IPAddress> ipAddresses)
    {
        return Redactor.Default.Format(ipAddresses);
    }

    public static Redactor.RedactedValue<IReadOnlyList<IpNetwork>> Format(IEnumerable<IpNetwork> ipNetworks)
    {
        return Redactor.Default.Format(ipNetworks);
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
        return Redactor.Default.RedactId(id);
    }

    public static string FormatSessionId(object? id)
    {
        return id?.ToString() ?? "<null>";
    }

    public static string FormatHostName(string? dnsName)
    {
        return Redactor.Default.RedactHostName(dnsName);
    }

    public static string FormatHostName(string? dnsName, int port)
    {
        return $"{FormatHostName(dnsName)}:{port}";
    }

    public static string FormatIpPacket(string ipPacketText)
    {
        return Redactor.Default.RedactPacketText(ipPacketText);
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