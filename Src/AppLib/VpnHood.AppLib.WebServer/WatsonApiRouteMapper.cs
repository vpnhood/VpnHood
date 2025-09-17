using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer;

internal class WatsonApiRouteMapper(WebserverLite server) 
    : IRouteMapper
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static Task Options(HttpContextBase ctx)
    {
        CorsMiddleware.AddCors(ctx);
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        return ctx.Response.Send();
    }

    public void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        server.Routes.PreAuthentication.Static.Add(method, path, async ctx => {
            try {
                // Add CORS to all requests centrally
                CorsMiddleware.AddCors(ctx);
                await handler(ctx);
            }
            catch (Exception ex) {
                await HandleException(ctx, ex);
            }
        });
    }

    public void AddParam(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        server.Routes.PreAuthentication.Parameter.Add(method, path, async ctx => {
            try {
                // Add CORS to all requests centrally
                CorsMiddleware.AddCors(ctx);
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
        if (NotExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        else if (AlreadyExistsException.Is(ex)) context.Response.StatusCode = (int)HttpStatusCode.Conflict;
        else if (ex is ArgumentException or InvalidOperationException) context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        else if (ex is UnauthorizedAccessException) context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        else context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        // Default to 500 for other exceptions
        context.Response.ContentType = "application/json";
        var errorResponse = ex.ToApiError();
        await context.SendJson(errorResponse);
    }

    public WatsonApiRouteMapper AddController(ControllerBase controller)
    {
        controller.AddRoutes(this);
        return this;
    }
}
