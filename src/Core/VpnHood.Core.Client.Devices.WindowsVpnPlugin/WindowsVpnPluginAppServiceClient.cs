using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
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
    private AppServiceConnection? _connection;
    private bool _disposed;

    /// <inheritdoc/>
    /// <remarks>
    /// The VPN host closes the connection when its session ends, or Windows when it ends the service's task, so a
    /// request that fails on the open connection is sent once more on a new one.
    /// </remarks>
    /// <exception cref="VpnServiceNotReadyException">
    /// Windows holds the package's VPN profile disconnected, so no VPN host has a session.
    /// </exception>
    /// <exception cref="VpnServiceUnreachableException">
    /// The app service could not be opened, or did not answer on a new connection.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();
        var message = new ValueSet { [WindowsVpnPluginAppService.MessageKey] = request.ToArray() };

        if (_connection is { } connection) {
            try {
                return await SendCore(connection, message, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch {
                CloseConnection();
            }
        }

        // As a TCP client finds no endpoint file: with the package's profile disconnected, after an update say, no VPN
        // host has a session, and opening the app service would only start one.
        if (await WindowsVpnPluginDevice.TryGetLiveProfiles().Vhc() is { Length: 0 })
            throw new VpnServiceNotReadyException("Windows holds the package's VPN profile disconnected.");

        connection = await Open(cancellationToken).Vhc();
        _connection = connection;
        try {
            return await SendCore(connection, message, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            CloseConnection();
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service is unreachable.", ex);
        }
    }

    private static async Task<Memory<byte>> SendCore(AppServiceConnection connection, ValueSet message,
        CancellationToken cancellationToken)
    {
        var response = await connection.SendMessageAsync(message).AsTask(cancellationToken).Vhc();
        if (response.Status != AppServiceResponseStatus.Success)
            throw new InvalidOperationException($"The VPN host did not answer. Status: {response.Status}");

        return response.Message.TryGetValue(WindowsVpnPluginAppService.MessageKey, out var value) &&
               value is byte[] payload
            ? payload
            : throw new InvalidDataException("The VPN host's answer carries no response.");
    }

    private static async Task<AppServiceConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new AppServiceConnection {
            AppServiceName = WindowsVpnPluginAppService.Name,
            PackageFamilyName = Package.Current.Id.FamilyName
        };

        AppServiceConnectionStatus status;
        try {
            status = await connection.OpenAsync().AsTask(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            connection.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new VpnServiceUnreachableException("The Windows VPN plug-in's service could not be opened.", ex);
        }

        if (status == AppServiceConnectionStatus.Success)
            return connection;

        connection.Dispose();
        throw new VpnServiceUnreachableException($"The Windows VPN plug-in's service could not be opened. Status: {status}");
    }

    private void CloseConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }

    /// <summary>Closes the connection to the app service.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseConnection();
    }
}
