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
        AddCors(ctx);
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        return ctx.Response.Send();
    }

    private void AddCors(HttpContextBase ctx)
    {
        var cors = VpnHoodApp.Instance.Features.IsDebugMode ? "*" : "https://localhost:8080, http://localhost:8080, https://localhost:8081, http://localhost:8081, http://localhost:30080";
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", cors);
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
    }

    public void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        _server.Routes.PreAuthentication.Static.Add(method, path, async ctx => {
            try {
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
        // Log the exception (you might want to use your logging framework here)
        VhLogger.Instance.LogError(ex, "Unhandled exception occurred while processing API request.");

        AddCors(ctx);
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
