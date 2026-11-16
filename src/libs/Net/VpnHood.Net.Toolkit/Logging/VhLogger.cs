using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the process's one log, and the formatting that keeps identifying values out of it.
/// </summary>
/// <remarks>
/// Its sinks are providers, added once by whoever owns them, before anything logs: an app's console, a service's system
/// log, a file. <see cref="MinLogLevel"/> is the log's own filter, read at each line, so no sink is rebuilt when the app's
/// log settings change it; a sink may refuse more. Redaction applies to the values passed through the <c>Format</c>
/// methods, not to messages.
/// </remarks>
public static class VhLogger
{
    private const string CategoryName = "VpnHood";

    // The filter reads MinLogLevel at each line; a matching rule also puts the factory's own default
    // level, Information, out of play.
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder =>
        builder.AddFilter((_, logLevel) => logLevel >= MinLogLevel));

    // The same object for the process's life: what the sinks and the level change is behind it.
    /// <summary>Gets the process's logger, the same object for the process's life.</summary>
    public static ILogger Instance { get; } = Factory.CreateLogger(CategoryName);

    // PreserveTypes carries the types iOS trimming must keep, and the trimmer keeps them only where
    // the call is reached: here, in the type every process touches.
    static VhLogger()
    {
        AotPreserveHelper.PreserveTypes();
    }

    /// <summary>Adds a sink to the log.</summary>
    /// <param name="loggerProvider">The sink's provider, which the log keeps for the process's life.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loggerProvider"/> is <see langword="null"/>.</exception>
    public static void AddProvider(ILoggerProvider loggerProvider)
    {
        Factory.AddProvider(loggerProvider);
    }

    /// <summary>
    /// Gets or sets the event ID of the Debug line <see cref="LogError"/> writes for a socket's ordinary close.
    /// </summary>
    public static EventId TcpCloseEventId { get; set; }

    /// <summary>
    /// The redactor this logger applies to the values passed through <c>Format</c>. Set only through
    /// <see cref="IsAnonymousMode" />, so that turning redaction off stays a single deliberate switch
    /// rather than something any caller can install. Callers that must redact whatever the logging
    /// settings say use <see cref="Utils.Redactor.Always" /> instead of this one.
    /// </summary>
    public static Redactor Redactor { get; private set; } = new(isAnonymousMode: true);

    /// <summary>
    /// Gets or sets a value indicating whether <c>Format</c> redacts identifying values: addresses, endpoints, networks,
    /// IDs and host names. The default is <see langword="true"/>.
    /// </summary>
    // the mode is fixed per redactor, so changing it swaps the instance rather than mutating one
    public static bool IsAnonymousMode {
        get => Redactor.IsAnonymousMode;
        set {
            if (Redactor.IsAnonymousMode != value)
                Redactor = new Redactor(value);
        }
    }

    /// <summary>Gets or sets the lowest level the log writes. The default is Information.</summary>
    public static LogLevel MinLogLevel { get; set; } = LogLevel.Information;

    /// <summary>Wraps an endpoint for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="endPoint">The endpoint, or <see langword="null"/>.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<EndPoint> Format(EndPoint? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    /// <summary>Wraps an endpoint for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="endPoint">The endpoint, or <see langword="null"/>.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<EndPoint> Format(IPEndPoint? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    /// <summary>Wraps an endpoint for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="endPoint">The endpoint, or <see langword="null"/>.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    /// <exception cref="InvalidOperationException">The endpoint is the default value, which holds no address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The endpoint's port is out of range.</exception>
    public static Redactor.RedactedValue<EndPoint> Format(IpEndPointValue? endPoint)
    {
        return Redactor.Format(endPoint);
    }

    /// <summary>Wraps an IP address for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="ipAddress">The address, or <see langword="null"/>.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<IPAddress> Format(IPAddress? ipAddress)
    {
        return Redactor.Format(ipAddress);
    }

    /// <summary>Wraps a network for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="ipNetwork">The network, or <see langword="null"/>.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<IpNetwork> Format(IpNetwork? ipNetwork)
    {
        return Redactor.Format(ipNetwork);
    }

    /// <summary>Wraps IP addresses for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="ipAddresses">The addresses.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<IReadOnlyList<IPAddress>> Format(IEnumerable<IPAddress> ipAddresses)
    {
        return Redactor.Format(ipAddresses);
    }

    /// <summary>Wraps networks for a log line, redacted by the current redactor when the line turns it into text.</summary>
    /// <remarks>
    /// In anonymous mode a globally routable address becomes a token, stable within the run; a private or local
    /// address, a port, a prefix length and an endpoint that is not an IP one stay as they are, and several values are
    /// joined by commas. A conversion or a copy of a sequence happens now; the redaction each time the text is made.
    /// </remarks>
    /// <param name="ipNetworks">The networks.</param>
    /// <returns>A wrapper that formats on each <c>ToString</c>, with the redactor captured by this call.</returns>
    public static Redactor.RedactedValue<IReadOnlyList<IpNetwork>> Format(IEnumerable<IpNetwork> ipNetworks)
    {
        return Redactor.Format(ipNetworks);
    }

    /// <summary>Formats an object's type for the log: its name.</summary>
    /// <param name="obj">The object, or <see langword="null"/>.</param>
    /// <returns>The type's name, or <c>&lt;null&gt;</c>.</returns>
    public static string FormatType(object? obj)
    {
        return obj?.GetType().Name ?? "<null>";
    }

    /// <summary>Formats a type for the log: its name.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The type's name.</returns>
    public static string FormatType<T>()
    {
        return typeof(T).Name;
    }

    /// <summary>Formats an identifying ID for the log. In anonymous mode only its second half shows, after <c>**</c>.</summary>
    /// <param name="id">The ID, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public static string FormatId(object? id)
    {
        return Redactor.RedactId(id);
    }

    /// <summary>Formats a session ID for the log, as it is: it is not redacted.</summary>
    /// <param name="id">The ID, or <see langword="null"/>.</param>
    /// <returns>The ID's text, or <c>&lt;null&gt;</c>.</returns>
    public static string FormatSessionId(object? id)
    {
        return id?.ToString() ?? "<null>";
    }

    /// <summary>
    /// Formats a host name for the log: a name that is an address or an endpoint is redacted as one; another, in anonymous
    /// mode, shows only its last four characters, and a name over eight characters its first two too.
    /// </summary>
    /// <param name="dnsName">The host name, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public static string FormatHostName(string? dnsName)
    {
        return Redactor.RedactHostName(dnsName);
    }

    /// <summary>Formats a host name and a port for the log, as <c>host:port</c>; the host as <see cref="FormatHostName(string)"/> does.</summary>
    /// <param name="dnsName">The host name, or <see langword="null"/>.</param>
    /// <param name="port">The port.</param>
    /// <returns>The text.</returns>
    public static string FormatHostName(string? dnsName, int port)
    {
        return $"{FormatHostName(dnsName)}:{port}";
    }

    /// <summary>
    /// Formats a packet's text for the log. In anonymous mode the addresses after its source and destination labels are
    /// redacted as <c>Format</c> redacts an address.
    /// </summary>
    /// <param name="ipPacketText">The packet's text.</param>
    /// <returns>The text.</returns>
    public static string FormatIpPacket(string ipPacketText)
    {
        return Redactor.RedactPacketText(ipPacketText);
    }

    /// <summary>
    /// Determines whether an exception, or one along its <see cref="Exception.InnerException"/> chain, is a disposal, a
    /// cancellation, or a socket error of an aborted, reset or refused connection, an aborted operation or a network
    /// reset, wherever it comes from.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>
    /// Logs an error, or, for what <see cref="IsSocketCloseException"/> accepts, a Debug line with
    /// <see cref="TcpCloseEventId"/> and the outer exception's message, without the exception.
    /// </summary>
    /// <param name="eventId">The error's event ID.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message template.</param>
    /// <param name="args">The template's arguments.</param>
    public static void LogError(EventId eventId, Exception ex, string message, params object?[] args)
    {
#pragma warning disable CA2254 // it is our log builder, not a simple logging
        // the exception's message as an argument: put into the template, its braces would be read as the
        // template's own
        if (IsSocketCloseException(ex)) {
            Instance.LogDebug(TcpCloseEventId, message + " Message: {ExceptionMessage}", [.. args, ex.Message]);
            return;
        }

        Instance.LogError(eventId, ex, message, args);
#pragma warning restore CA2254
    }
}
