using System.Net;
using System.Net.Mime;
using EmbedIO;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;

namespace VpnHood.AppLib.WebServer;

internal static class ExceptionHandler
{
    public static Task DataResponseForException(IHttpContext context, Exception ex)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        if (ex is ApiException apiException) {
            context.Response.StatusCode = apiException.StatusCode;
            throw new HttpException(apiException.StatusCode, ex.Message, apiException.ToApiError());
        }

        // set correct https status code depends on exception
        if (NotExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        else if (AlreadyExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.Conflict;
        else if (ex is UnauthorizedAccessException) context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        else context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        // create portable exception
        throw new HttpException(HttpStatusCode.BadRequest, ex.Message, ex.ToApiError());
    }

    public static Task DataResponseForHttpException(IHttpContext context, IHttpException httpException)
    {
        if (httpException.DataObject is ApiError)
            return ResponseSerializer.Json(context, httpException.DataObject);

        return context.SendStandardHtmlAsync(context.Response.StatusCode);
    }
}