using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/proxy-nodes")]
public class ProxyNodeController : ControllerBase, IProxyNodeController
{
    [HttpGet]
    public Task<AppProxyNodeInfo[]> List()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("parse")]
    public Task<AppProxyNodeInfo> Parse(string text, ProxyNodeDefaults defaults)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut]
    public Task<AppProxyNodeInfo> Update(Uri url, ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost]
    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete]
    public Task Delete(Uri url)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("import")]
    public Task Import(string text, bool removeOld)
    {
        throw new SwaggerOnlyException();
    }
}
