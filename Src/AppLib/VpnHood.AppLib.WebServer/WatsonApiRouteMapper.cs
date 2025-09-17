using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.Core.Toolkit.Logging;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer;

internal class WatsonApiRouteMapper : IRouteMapper
{
    private readonly WebserverLite _server;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WatsonApiRouteMapper(WebserverLite server)
    {
        _server = server;
        MapRoutes();
    }

    private Task Options(HttpContextBase ctx)
    {
        CorsMiddleware.AddCors(ctx);
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        return ctx.Response.Send();
    }

    public void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        _server.Routes.PreAuthentication.Static.Add(method, path, async ctx => {
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
        _server.Routes.PreAuthentication.Parameter.Add(method, path, async ctx => {
            try {
                // Add CORS to all requests centrally
                CorsMiddleware.AddCors(ctx);
                await handler(ctx);
            }
            catch (Exception ex) {
                await HandleException(ctx, ex);
            }
        });
        _server.Routes.PreAuthentication.Parameter.Add(HttpMethod.OPTIONS, path, Options);
    }

    private async Task HandleException(HttpContextBase ctx, Exception ex)
    {
        // Log the exception
        VhLogger.Instance.LogError(ex, "Unhandled exception occurred while processing API request.");

        // CORS is already added in the wrapper, no need to add it again
        ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        ctx.Response.ContentType = "application/json";

        var errorResponse = JsonSerializer.Serialize(new {
            error = ex.Message,
            type = ex.GetType().Name
        }, _jsonOptions);

        await ctx.Response.Send(errorResponse);
    }

    private void MapRoutes()
    {
        var appController = new AppController();
        var clientProfileController = new ClientProfileController();
        var accountController = new AccountController();
        var billingController = new BillingController();

        appController.AddRoutes(this);
        clientProfileController.AddRoutes(this);
        accountController.AddRoutes(this);
        billingController.AddRoutes(this);
    }
}
