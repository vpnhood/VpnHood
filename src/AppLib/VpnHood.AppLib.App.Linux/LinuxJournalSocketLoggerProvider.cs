using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Linux;

// The journal as the window's sink, for the process's life, under the instance's name: where
// LinuxJournalLoggerProvider needs stdout to be the journal, as a unit's is, this writes to the
// journal's socket. One logger serves every category, without the level word and the scopes: the
// entries name the app, not a category, and the journal shows their priority.
public sealed class LinuxJournalSocketLoggerProvider(string identifier) : ILoggerProvider
{
    private readonly Socket _socket = new(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
    private TextLogger? _logger;

    public ILogger CreateLogger(string categoryName)
    {
        return _logger ??= new TextLogger(new LinuxJournalSocketLogger(_socket, identifier),
            singleLine: false, includeTime: false, includeScopes: false);
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}
