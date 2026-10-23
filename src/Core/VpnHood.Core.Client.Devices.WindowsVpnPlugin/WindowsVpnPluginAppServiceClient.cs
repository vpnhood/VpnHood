using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The app's end of the VPN host's control channel, the package's app service. Opening it makes Windows run the
// service's task in the VPN host, which keeps the host running while the connection stays open.
public sealed class WindowsVpnPluginAppServiceClient : IMessageClient
{
    private readonly AsyncLock _sendLock = new();
    private AppServiceConnection? _connection;
    private bool _disposed;

    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).Vhc();
        var message = new ValueSet { [WindowsVpnPluginAppServiceListener.MessageKey] = request.ToArray() };

        // The VPN host closes the connection when its session ends, or Windows when it ends the service's task:
        // a failure on the open one opens another, once.
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
        if (await WindowsVpnPluginService.TryGetLiveProfiles().Vhc() is { Length: 0 })
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

        return response.Message.TryGetValue(WindowsVpnPluginAppServiceListener.MessageKey, out var value) &&
               value is byte[] payload
            ? payload
            : throw new InvalidDataException("The VPN host's answer carries no response.");
    }

    private static async Task<AppServiceConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new AppServiceConnection {
            AppServiceName = WindowsVpnPluginAppServiceListener.AppServiceName,
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseConnection();
    }
}
