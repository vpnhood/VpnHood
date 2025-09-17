using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.WebServer.Api;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ClientProfileController : ControllerBase, IClientProfileController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public override void AddRoutes(IRouteMapper mapper)
    {
        mapper.AddStatic(HttpMethod.PUT, "/api/client-profiles/access-keys", async ctx => {
            var accessKey = ctx.GetQueryParameterString("accessKey") ?? string.Empty;
            var res = await AddByAccessKey(accessKey);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.GET, "/api/client-profiles/{id}", async ctx => {
            if (!Guid.TryParse(ctx.GetRouteParameter("id"), out var id)) { 
                ctx.Response.StatusCode = 400; 
                await ctx.Response.Send(); 
                return; 
            }
            var res = await Get(id);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.PATCH, "/api/client-profiles/{id}", async ctx => {
            if (!Guid.TryParse(ctx.Request.Url.Parameters["id"], out var id)) { 
                ctx.Response.StatusCode = 400; 
                await ctx.Response.Send(); 
                return; 
            }
            var body = ctx.ReadJson<ClientProfileUpdateParams>();
            var res = await Update(id, body);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.DELETE, "/api/client-profiles/{id}", async ctx => {
            if (!Guid.TryParse(ctx.Request.Url.Parameters["id"], out var id)) { 
                ctx.Response.StatusCode = 400; 
                await ctx.Response.Send(); 
                return; 
            }
            await Delete(id);
            await ctx.SendJson(new { ok = true });
        });
    }

    public Task<ClientProfileInfo> AddByAccessKey(string accessKey)
    {
        var clientProfile = App.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        var clientProfile = App.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        var clientProfile = App.ClientProfileService.Update(clientProfileId, updateParams);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task Delete(Guid clientProfileId)
    {
        if (clientProfileId == App.CurrentClientProfileInfo?.ClientProfileId)
            return App.Disconnect();

        App.ClientProfileService.Delete(clientProfileId);
        return Task.CompletedTask;
    }
}