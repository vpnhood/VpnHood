using System.Net;
using System.Net.Mime;
using EmbedIO;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.WebServer;

internal static class ExceptionHandler
{
    public static Task DataResponseForException(IHttpContext context, Exception ex)
    {
        if (ex is ApiException apiException) {
            var apiError = new ApiError(apiException.ExceptionTypeName ?? nameof(ApiException), ex.Message) {
                TypeFullName = apiException.ExceptionTypeFullName,
                InnerMessage = apiException.InnerException?.Message
            };

            foreach (var key in apiException.Data) {
                if (key is string keyStr)
                    apiError.Data.TryAdd(keyStr, ex.Data[keyStr]?.ToString());
                apiError.Data.TryAdd("InnerStatusCode", apiException.StatusCode.ToString());
            }

            context.Response.ContentType = MediaTypeNames.Application.Json;
            context.Response.StatusCode = apiException.StatusCode;
            throw new HttpException(apiException.StatusCode, apiError.Message, apiError);
        }
        else {
            // set correct https status code depends on exception
            if (NotExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            else if (AlreadyExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            else if (ex is UnauthorizedAccessException) context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            else context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;

            // create portable exception
            var apiError = new ApiError(ex);
            throw new HttpException(HttpStatusCode.BadRequest, apiError.Message, apiError);
        }
    }

    public static Task DataResponseForHttpException(IHttpContext context, IHttpException httpException)
    {
        if (httpException.DataObject is ApiError)
            return ResponseSerializer.Json(context, httpException.DataObject);

        return context.SendStandardHtmlAsync(context.Response.StatusCode);
    }
}