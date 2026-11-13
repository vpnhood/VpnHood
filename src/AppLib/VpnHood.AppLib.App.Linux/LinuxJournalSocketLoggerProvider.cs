using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Linux;

// The journal as the window's sink, for the process's life, under the instance's name: where
// LinuxJournalLoggerProvider needs stdout to be the journal, as a unit's is, this writes to the
// journal's socket. One logger serves every category, without the level word and the scopes: the
// entries name the app, not a category, and the journal shows their priority.
/// <summary>
/// Provides loggers that write to the systemd journal's socket, for a process whose standard output is not the journal,
/// as the window a desktop session starts.
/// </summary>
/// <remarks>
/// One logger serves every category, without the level word and the scopes: the entries name the app, and the journal
/// shows their priority. It writes warnings and errors only.
/// </remarks>
/// <param name="identifier">The name the journal files the entries under (<c>journalctl -t</c>).</param>
public sealed class LinuxJournalSocketLoggerProvider(string identifier) : ILoggerProvider
{
    private readonly Socket _socket = new(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
    private TextLogger? _logger;

    /// <inheritdoc/>
    /// <remarks>It gives the same logger for every category.</remarks>
    public ILogger CreateLogger(string categoryName)
    {
        return _logger ??= new TextLogger(new LinuxJournalSocketLogger(_socket, identifier),
            singleLine: false, includeTime: false, includeScopes: false, includeLevel: false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _socket.Dispose();
    }
}
