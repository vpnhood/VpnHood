using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.AppLogic.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLogic.App;

internal static class AppExceptionConverter
{
    public static Exception? ApiErrorToException(ApiError apiError)
    {
        return ApiErrorToAppException(apiError) ?? ClientExceptionConverter.ApiErrorToException(apiError);
    }

    // the app's own exceptions alone, as an ad's, which the client's converter does not know
    public static Exception? ApiErrorToAppException(ApiError apiError)
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

        if (apiError.Is<AdBlockerException>())
            exception = new AdBlockerException(apiError.Message);

        // app
        if (apiError.Is<NoInternetException>())
            exception = new NoInternetException();

        if (apiError.Is<NoStableVpnException>())
            exception = new NoStableVpnException();

        if (exception != null)
            apiError.ExportData(exception.Data);

        return exception;
    }
}