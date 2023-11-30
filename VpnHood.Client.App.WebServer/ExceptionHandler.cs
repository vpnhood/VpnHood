using System.Collections;
using System.Net;
using System.Net.Mime;
using EmbedIO;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;

namespace VpnHood.Client.App.WebServer;

internal static class ExceptionHandler
{
    private static Type GetExceptionType(Exception ex)
    {
        if (AlreadyExistsException.Is(ex)) return typeof(AlreadyExistsException);
        if (NotExistsException.Is(ex)) return typeof(NotExistsException);
        return ex.GetType();
    }

    public static Task DataResponseForException(IHttpContext context, Exception ex)
    {
        // set correct https status code depends on exception
        if (NotExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        else if (AlreadyExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.Conflict;
        else if (ex is UnauthorizedAccessException) context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        else context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var typeFullName = GetExceptionType(ex).FullName;
        var message = ex.Message;

        // set optional information
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var error = new ApiException.ServerException
        {
            Data = new Dictionary<string, string?>(),
            TypeName = GetExceptionType(ex).Name,
            TypeFullName = typeFullName,
            Message = message
        };

        // add inner message if exists
        if (!string.IsNullOrEmpty(ex.InnerException?.Message))
            error.Data.TryAdd("InnerMessage", ex.InnerException?.Message);

        foreach (DictionaryEntry item in ex.Data)
        {
            var key = item.Key.ToString();
            if (key != null)
                error.Data.TryAdd(key, item.Value?.ToString());
        }

        throw new HttpException(HttpStatusCode.BadRequest, error.Message, error);
    }

    public static Task DataResponseForHttpException(IHttpContext context, IHttpException httpException)
    {
        if (httpException.DataObject is ApiException.ServerException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return ResponseSerializer.Json(context, httpException.DataObject);
        }

        return context.SendStandardHtmlAsync(context.Response.StatusCode);
    }
}