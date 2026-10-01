using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Logging;

// The process's one log. Its sinks are providers, added once by whoever owns them, before anything
// logs: the head its console, a service's host its system log, the app its file; .NET's factory
// fans out to every sink and asks each what it takes. The level is the one filter, read at each
// line, so no sink is rebuilt when the app's log settings change it.
public static class VhLogger
{
    private const string CategoryName = "VpnHood";

    // The filter reads MinLogLevel at each line; a matching rule also puts the factory's own default
    // level, Information, out of play.
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder =>
        builder.AddFilter((_, logLevel) => logLevel >= MinLogLevel));

    // The same object for the process's life: what the sinks and the level change is behind it.
    public static ILogger Instance { get; } = Factory.CreateLogger(CategoryName);

    // PreserveTypes carries the types iOS trimming must keep, and the trimmer keeps them only where
    // the call is reached: here, in the type every process touches.
    static VhLogger()
    {
        AotPreserveHelper.PreserveTypes();
    }

    public static void AddProvider(ILoggerProvider loggerProvider)
    {
        Factory.AddProvider(loggerProvider);
    }

    public static EventId TcpCloseEventId { get; set; }

    /// <summary>
    /// The redactor this logger applies to the values passed through <c>Format</c>. Set only through
    /// <see cref="IsAnonymousMode" />, so that turning redaction off stays a single deliberate switch
    /// rather than something any caller can install. Callers that must redact whatever the logging
    /// settings say use <see cref="Utils.Redactor.Always" /> instead of this one.
    /// </summary>
    public static Redactor Redactor { get; private set; } = new(isAnonymousMode: true);

    // the mode is fixed per redactor, so changing it swaps the instance rather than mutating one
    public static bool IsAnonymousMode {
        get => Redactor.IsAnonymousMode;
        set {
            if (Redactor.IsAnonymousMode != value)
                Redactor = new Redactor(value);
        }
    }

    public static LogLevel MinLogLevel { get; set; } = LogLevel.Information;

    public static Redactor.RedactedValue<EndPoint> Format(EndPoint? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    public static Redactor.RedactedValue<EndPoint> Format(IPEndPoint? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    public static Redactor.RedactedValue<EndPoint> Format(IpEndPointValue? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    public static Redactor.RedactedValue<IPAddress> Format(IPAddress? ipAddress)
    {
        return Redactor.Format(ipAddress);
    }

    public static Redactor.RedactedValue<IpNetwork> Format(IpNetwork? ipNetwork)
    {
        return Redactor.Format(ipNetwork);
    }

    public static Redactor.RedactedValue<IReadOnlyList<IPAddress>> Format(IEnumerable<IPAddress> ipAddresses)
    {
        return Redactor.Format(ipAddresses);
    }

    public static Redactor.RedactedValue<IReadOnlyList<IpNetwork>> Format(IEnumerable<IpNetwork> ipNetworks)
    {
        return Redactor.Format(ipNetworks);
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
        return Redactor.RedactId(id);
    }

    public static string FormatSessionId(object? id)
    {
        return id?.ToString() ?? "<null>";
    }

    public static string FormatHostName(string? dnsName)
    {
        return Redactor.RedactHostName(dnsName);
    }

    public static string FormatHostName(string? dnsName, int port)
    {
        return $"{FormatHostName(dnsName)}:{port}";
    }

    public static string FormatIpPacket(string ipPacketText)
    {
        return Redactor.RedactPacketText(ipPacketText);
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
}
