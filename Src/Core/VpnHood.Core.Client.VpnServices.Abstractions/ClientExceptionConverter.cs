using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public static class ClientExceptionConverter
{
    public static Exception ApiErrorToException(ApiError apiError)
    {
        Exception? exception = null;

        // common
        if (apiError.Is<MaintenanceException>())
            exception = new MaintenanceException();

        if (apiError.Is<SessionException>())
            exception = new SessionException(apiError);

        if (apiError.Is<UiContextNotAvailableException>())
            exception = new UiContextNotAvailableException();

        // ad
        if (apiError.Is<AdException>())
            exception = new AdException(apiError.Message);
        
        if (apiError.Is<ShowAdException>())
            exception = new ShowAdException(apiError.Message);

        if (apiError.Is<ShowAdNoUiException>())
            exception = new ShowAdNoUiException(apiError.Message);

        if (apiError.Is<LoadAdException>())
            exception = new LoadAdException(apiError.Message);

        // service
        if (apiError.Is<AutoStartNotSupportedException>())
            exception = new AutoStartNotSupportedException(apiError.Message);

        // client
        if (apiError.Is<NoInternetException>())
            exception = new NoInternetException();

        if (apiError.Is<NoStableVpnException>())
            exception = new NoStableVpnException();

        if (apiError.Is<ConnectionTimeoutException>())
            exception = new ConnectionTimeoutException(apiError.Message);

        if (apiError.Is<UnreachableServerException>())
            exception = new UnreachableServerException(apiError.Message);

        if (apiError.Is<UnreachableServerLocationException>())
            exception = new UnreachableServerLocationException(apiError.Message);

        if (exception != null)
            apiError.ExportData(exception.Data);

        return exception ?? apiError.ToException();
    }
}