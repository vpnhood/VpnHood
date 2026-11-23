using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

/// <summary>
/// Represents the app's end of the VPN host's control channel, the package's app service.
/// </summary>
/// <remarks>
/// Opening it makes Windows run the service's task in the VPN host, which keeps the host running while the connection
/// stays open.
/// </remarks>
internal sealed class WindowsVpnPluginAppServiceClient : IMessageClient
{
    private readonly AsyncLock _sendLock = new();
    // The connection kept, the one being opened and whether the host closed that one, and the client's close, one at a
    // time. A kept connection is closed by whoever takes it from the client; one being opened, by its opener
    private readonly Lock _connectionLock = new();
    private readonly CancellationTokenSource _disposeCts = new(); // the client's close, which ends an open under way
    private AppServiceConnection? _connection;
    private AppServiceConnection? _openingConnection;
    private bool _isOpeningClosed;
    private bool _disposed;

    /// <inheritdoc/>
    /// <remarks>
    /// The VPN host closes the connection some time after its session ends, or Windows when it ends the service's task:
    /// a connection closed so takes no request, and the next opens a new one. A request that fails is not sent again, as
    /// the host may have taken it before its answer failed: a disconnect sent twice could end the next session. The
    /// connection closes, and the next request opens a new one, as it does after a request whose caller stopped waiting
    /// for its answer, which may still hold the connection.
    /// </remarks>
    /// <exception cref="VpnServiceNotReadyException">
    /// Windows holds the package's VPN profile disconnected, so no VPN host has a session.
    /// </exception>
    /// <exception cref="VpnServiceUnreachableException">The app service could not be opened, or did not answer.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();
        var message = new ValueSet { [WindowsVpnPluginAppService.MessageKey] = request.ToArray() };
        var connection = _connection ?? await OpenConnection(cancellationToken).Vhc();
        try {
            return await SendCore(connection, message, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            CloseConnection(connection);
            throw;
        }
        catch (Exception ex) {
            CloseConnection(connection);
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service is unreachable.", ex);
        }
    }

    // The client's close ends a lookup or an open under way, which its caller may wait for with no time of its own: the
    // caller hears of the close, whatever the lookup or the open failed with as it ended
    private async Task<AppServiceConnection> OpenConnection(CancellationToken cancellationToken)
    {
        using var openCts = CreateOpenCts(cancellationToken);
        try {
            return await OpenConnectionCore(openCts.Token).Vhc();
        }
        catch (Exception ex) when (_disposed && !cancellationToken.IsCancellationRequested) {
            throw new ObjectDisposedException(GetType().FullName, ex);
        }
    }

    // the caller's token, which the client's close cancels too; taken under the close's lock, so a closed client opens
    // nothing
    private CancellationTokenSource CreateOpenCts(CancellationToken cancellationToken)
    {
        lock (_connectionLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        }
    }

    // As a TCP client finds no endpoint file: with the package's profile disconnected, after an update say, no VPN host
    // has a session, and opening the app service would only start one. The close's handler is on the connection before it
    // opens, so no close goes unheard; one the host closed as it opened, or opened as the client closed, is closed too.
    private async Task<AppServiceConnection> OpenConnectionCore(CancellationToken cancellationToken)
    {
        if (await WindowsVpnPluginDevice.TryGetLiveProfiles(cancellationToken).Vhc() is { Length: 0 })
            throw new VpnServiceNotReadyException("Windows holds the package's VPN profile disconnected.");

        var connection = new AppServiceConnection {
            AppServiceName = WindowsVpnPluginAppService.Name,
            PackageFamilyName = Package.Current.Id.FamilyName
        };

        connection.ServiceClosed += Connection_ServiceClosed;
        try {
            await Open(connection, cancellationToken).Vhc();
            if (TryKeepConnection(connection))
                return connection;

            ObjectDisposedException.ThrowIf(_disposed, this);
            throw new VpnServiceUnreachableException(
                "The Windows VPN plug-in's service closed the connection as it opened.");
        }
        catch {
            connection.Dispose();
            throw;
        }
        finally {
            lock (_connectionLock) {
                if (_openingConnection == connection)
                    _openingConnection = null;
            }
        }
    }

    // Whether the connection opened is kept: not where the host closed it as it opened, or the client is closed. It is
    // no longer the one being opened either way, in the same turn, so a close heard after finds it kept, and closes it
    private bool TryKeepConnection(AppServiceConnection connection)
    {
        lock (_connectionLock) {
            _openingConnection = null;
            if (_disposed || _isOpeningClosed)
                return false;

            _connection = connection;
            return true;
        }
    }

    // a request already ended is not handed to the connection, which would deliver it whatever its token said after
    private static async Task<Memory<byte>> SendCore(AppServiceConnection connection, ValueSet message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await connection.SendMessageAsync(message).AsTask(cancellationToken).Vhc();
        if (response.Status != AppServiceResponseStatus.Success)
            throw new InvalidOperationException($"The VPN host did not answer. Status: {response.Status}");

        return response.Message.TryGetValue(WindowsVpnPluginAppService.MessageKey, out var value) &&
               value is byte[] payload
            ? payload
            : throw new InvalidDataException("The VPN host's answer carries no response.");
    }

    // the open, which its caller closes where it fails
    private async Task Open(AppServiceConnection connection, CancellationToken cancellationToken)
    {
        var status = await TryOpen(connection, cancellationToken).Vhc();
        if (status != AppServiceConnectionStatus.Success)
            throw new VpnServiceUnreachableException(
                $"The Windows VPN plug-in's service could not be opened. Status: {status}");
    }

    private async Task<AppServiceConnectionStatus> TryOpen(AppServiceConnection connection,
        CancellationToken cancellationToken)
    {
        try {
            return await StartOpen(connection, cancellationToken).AsTask(cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !_disposed) {
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service could not be opened.", ex);
        }
    }

    // The open's start, the connection marked as the one being opened, under the close's lock: a closed client, or a
    // request already ended, starts none, which would run the service's task in the VPN host; a close or an end after it
    // ends it by the open's token
    private IAsyncOperation<AppServiceConnectionStatus> StartOpen(AppServiceConnection connection,
        CancellationToken cancellationToken)
    {
        lock (_connectionLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            _openingConnection = connection;
            _isOpeningClosed = false;
            return connection.OpenAsync();
        }
    }

    // The VPN host's close of a connection, which a request would otherwise fail on, unsent. One being opened is marked,
    // for its opener to close; another, closed where it is the kept one
    private void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
    {
        lock (_connectionLock) {
            if (sender == _openingConnection) {
                _isOpeningClosed = true;
                return;
            }
        }

        CloseConnection(sender);
    }

    // a connection a request used, or the host closed, which no further request takes: closed by whoever takes it from
    // the client, once
    private void CloseConnection(AppServiceConnection connection)
    {
        lock (_connectionLock) {
            if (_connection != connection)
                return;

            _connection = null;
        }

        connection.Dispose();
    }

    /// <summary>Closes the connection to the app service, and ends a lookup or an open under way.</summary>
    public void Dispose()
    {
        lock (_connectionLock) {
            if (_disposed)
                return;

            _disposed = true;
        }

        // none is published from here: a send that opens one closes it itself
        _disposeCts.TryCancel();
        _disposeCts.Dispose();
        if (_connection is { } connection)
            CloseConnection(connection);
    }
}
