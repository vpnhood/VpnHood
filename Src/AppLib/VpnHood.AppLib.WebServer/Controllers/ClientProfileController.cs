using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ClientProfileController : ControllerBase, IClientProfileController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/client-profiles/";

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "access-keys", async ctx => {
            var accessKey = ctx.GetQueryParameter<string>("accessKey");
            var res = await AddByAccessKey(accessKey);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await Get(id);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.PATCH, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var body = ctx.ReadJson<ClientProfileUpdateParams>();
            var res = await Update(id, body);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            await Delete(id);
            await ctx.SendNoContent();
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