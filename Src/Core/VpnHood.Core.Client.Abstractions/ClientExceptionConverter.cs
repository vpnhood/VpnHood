using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Client.Abstractions;

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

        if (apiError.Is<AdException>())
            exception = new AdException(apiError.Message);
        
        if (apiError.Is<ShowAdException>())
            exception = new ShowAdException(apiError.Message);

        if (apiError.Is<ShowAdNoUiException>())
            exception = new ShowAdNoUiException(apiError.Message);

        if (apiError.Is<LoadAdException>())
            exception = new LoadAdException(apiError.Message);

        // client
        if (apiError.Is<NoInternetException>())
            exception = new NoInternetException();

        if (apiError.Is<NoStableVpnException>())
            exception = new NoStableVpnException();

        if (apiError.Is<UnreachableServer>())
            exception = new UnreachableServer(apiError.Message);

        if (apiError.Is<UnreachableServerLocation>())
            exception = new UnreachableServerLocation(apiError.Message);

        if (exception != null)
            apiError.ExportData(exception.Data);

        return exception ?? apiError.ToException();
    }
}