using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Provides the conversion of an error the VPN service reported back into the exception it stands for.
/// </summary>
public static class ClientExceptionConverter
{
    /// <summary>
    /// Makes the exception an error stands for, where it is one of the client's or the service's that the app handles
    /// by type, with the error's data.
    /// </summary>
    /// <param name="apiError">The error.</param>
    /// <returns>The exception, or <see langword="null"/> where the error's type is not one of them.</returns>
    /// <exception cref="ArgumentException">A session's error carries no response.</exception>
    /// <exception cref="System.Text.Json.JsonException">A session's error carries a response that is not JSON.</exception>
    /// <exception cref="FormatException">An endpoint in a session's error's response is not valid.</exception>
    /// <remarks>
    /// A <see cref="ServerAddressChangedException"/> made here has no
    /// <see cref="ServerAddressChangedException.NewAddresses"/>: they stay in the service's process.
    /// </remarks>
    public static Exception? ApiErrorToException(ApiError apiError)
    {
        Exception? exception = null;

        // common
        if (apiError.Is<MaintenanceException>())
            exception = new MaintenanceException();

        if (apiError.Is<SessionException>())
            exception = new SessionException(apiError);

        if (apiError.Is<UiContextNotAvailableException>())
            exception = new UiContextNotAvailableException();

        // service
        if (apiError.Is<AlwaysOnNotAllowedException>())
            exception = new AlwaysOnNotAllowedException(apiError.Message);

        if (apiError.Is<VpnServiceRevokedException>())
            exception = new VpnServiceRevokedException(apiError.Message);

        if (apiError.Is<VpnServiceContractException>())
            exception = new VpnServiceContractException(apiError.Message);

        // client
        if (apiError.Is<ConnectionTimeoutException>())
            exception = new ConnectionTimeoutException(apiError.Message);

        if (apiError.Is<UnreachableServerException>())
            exception = new UnreachableServerException(apiError.Message);

        if (apiError.Is<UnreachableProxyServerException>())
            exception = new UnreachableProxyServerException(apiError.Message);

        if (apiError.Is<UnreachableServerLocationException>())
            exception = new UnreachableServerLocationException(apiError.Message);

        if (apiError.Is<VpnAdapterFailedException>())
            exception = new VpnAdapterFailedException(apiError.Message);

        if (apiError.Is<ServerAddressChangedException>())
            exception = new ServerAddressChangedException(apiError.Message);

        if (apiError.Is<ServerIpVersionUnsupportedException>())
            exception = new ServerIpVersionUnsupportedException(apiError.Message);

        if (exception != null)
            apiError.ExportData(exception.Data);

        return exception;
    }
}