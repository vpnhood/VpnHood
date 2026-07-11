using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.AppLib.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib;

public static class AppExceptionConverter
{
    public static Exception? ApiErrorToException(ApiError apiError)
    {
        Exception? exception = null;

        // ad
        if (apiError.Is<AdException>())
            exception = new AdException(apiError.Message);

        if (apiError.Is<ShowAdException>())
            exception = new ShowAdException(apiError.Message);

        if (apiError.Is<ShowAdNoUiException>())
            exception = new ShowAdNoUiException(apiError.Message);

        if (apiError.Is<LoadAdException>())
            exception = new LoadAdException(apiError.Message);

        // app
        if (apiError.Is<NoInternetException>())
            exception = new NoInternetException();

        if (apiError.Is<NoStableVpnException>())
            exception = new NoStableVpnException();

        if (exception != null)
            apiError.ExportData(exception.Data);

        return exception ?? ClientExceptionConverter.ApiErrorToException(apiError);
    }
}