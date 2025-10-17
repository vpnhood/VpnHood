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
    [HttpGet("device")]
    public Task<AppProxyNodeInfo?> GetDevice()
    {
        throw new SwaggerOnlyException();
    }

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

    [HttpPut("{proxyNodeId}")]
    public Task<AppProxyNodeInfo> Update(string proxyNodeId, ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost]
    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete("{proxyNodeId}")]
    public Task Delete(string proxyNodeId)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("reset-state")]
    public Task ResetState()
    {
        throw new NotImplementedException();
    }

    [HttpPost("Import")]
    public Task Import(string text, bool removeOld)
    {
        throw new SwaggerOnlyException();
    }
}
