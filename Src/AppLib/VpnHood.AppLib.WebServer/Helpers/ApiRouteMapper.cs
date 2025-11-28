using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Helpers;

public class ApiRouteMapper(WebserverLite server, bool isDebugMode)
    : IRouteMapper
{
    private Task Options(HttpContextBase ctx)
    {
        CorsMiddleware.AddCors(ctx, isDebugMode);
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        return ctx.Response.Send();
    }

    public void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        server.Routes.PreAuthentication.Static.Add(method, path, async ctx => {
            try {
                // Add CORS to all requests centrally
                CorsMiddleware.AddCors(ctx, isDebugMode);
                await handler(ctx);
            }
            catch (Exception ex) {
                await HandleException(ctx, ex);
            }
        });
        // Ensure preflight requests succeed for static routes
        server.Routes.PreAuthentication.Static.Add(HttpMethod.OPTIONS, path, Options);
    }

    public void AddParam(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        server.Routes.PreAuthentication.Parameter.Add(method, path, async ctx => {
            try {
                // Add CORS to all requests centrally
                CorsMiddleware.AddCors(ctx, isDebugMode);
                await handler(ctx);
            }
            catch (Exception ex) {
                await HandleException(ctx, ex);
            }
        });
        server.Routes.PreAuthentication.Parameter.Add(HttpMethod.OPTIONS, path, Options);
    }

    private static async Task HandleException(HttpContextBase context, Exception ex)
    {
        // Log the exception
        VhLogger.Instance.LogError(ex, "Unhandled exception occurred while processing API request.");

        // CORS is already added in the wrapper, no need to add it again

        // set correct https status code depends on exception
        var statusCode = HttpStatusCode.InternalServerError;
        if (NotExistsException.Is(ex)) statusCode = HttpStatusCode.NotFound;
        else if (AlreadyExistsException.Is(ex)) statusCode = HttpStatusCode.Conflict;
        else if (ex is ArgumentException or InvalidOperationException) statusCode = HttpStatusCode.BadRequest;
        else if (ex is UnauthorizedAccessException) statusCode = HttpStatusCode.Forbidden;

        // Default to 500 for other exceptions
        var apiError = ex.ToApiError();
        apiError.Data.TryAdd("HttpStatusCode", statusCode.ToString());
        await context.SendJson(apiError, (int)statusCode);
    }

    public ApiRouteMapper AddController(ControllerBase controller)
    {
        controller.AddRoutes(this);
        return this;
    }
}