using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.AppLib.App.Linux;

// The journal for a process whose stdout is not the journal - the window, which a desktop session
// starts: each entry one datagram on the journal's own socket, filed under the identifier it is given
// (journalctl -t). Warnings and errors only, as the window's on Windows. The journal stamps each entry
// and shows its priority, so its provider asks TextLogger for neither.
public sealed class LinuxJournalSocketLogger(Socket socket, string identifier) : ILogger
{
    private static readonly UnixDomainSocketEndPoint JournalSocket = new("/run/systemd/journal/socket");

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel is >= LogLevel.Warning and < LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        try {
            socket.SendTo(CreateEntry(identifier, logLevel, formatter(state, exception).TrimEnd()), JournalSocket);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) {
            // no journal to take it, as on a system without systemd: an entry that cannot be written is
            // not the caller's failure
        }
    }

    // The journal's native form: a field per line as NAME=value, but the message by its length, which
    // lets it keep its line breaks, so an exception stays one entry.
    internal static byte[] CreateEntry(string identifier, LogLevel logLevel, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var messageLength = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(messageLength, (ulong)messageBytes.Length);

        using var entry = new MemoryStream();
        entry.Write(Encoding.UTF8.GetBytes(
            $"PRIORITY={LinuxJournalLogger.GetPriority(logLevel)}\nSYSLOG_IDENTIFIER={identifier}\nMESSAGE\n"));
        entry.Write(messageLength);
        entry.Write(messageBytes);
        entry.WriteByte((byte)'\n');
        return entry.ToArray();
    }
}
