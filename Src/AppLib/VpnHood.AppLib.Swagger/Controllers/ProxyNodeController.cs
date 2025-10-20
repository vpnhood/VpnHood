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
    /// <summary>
    /// Get the device information
    /// </summary>
    [HttpGet("device")]
    public Task<AppProxyNodeInfo?> GetDevice()
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// List all proxy nodes
    /// </summary>
    [HttpGet]
    public Task<AppProxyNodeInfo[]> List()
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Parse and create a proxy node from the provided text
    /// </summary>
    /// <param name="text">The text to parse</param>
    /// <param name="defaults">The default settings for the proxy node</param>
    [HttpPost("parse")]
    public Task<AppProxyNodeInfo> Parse([FromQuery] string text, [FromBody] ProxyNodeDefaults defaults)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Update an existing proxy node
    /// </summary>
    /// <param name="proxyNodeId">The ID of the proxy node to update</param>
    /// <param name="proxyNode">The updated proxy node data</param>
    [HttpPut("{proxyNodeId}")]
    public Task<AppProxyNodeInfo> Update(string proxyNodeId, [FromBody] ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Add a new proxy node
    /// </summary>
    /// <param name="proxyNode">The proxy node to add</param>
    [HttpPost]
    public Task<AppProxyNodeInfo> Add([FromBody] ProxyNode proxyNode)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Delete a proxy node by its ID
    /// </summary>
    /// <param name="proxyNodeId">The ID of the proxy node to delete</param>
    [HttpDelete("{proxyNodeId}")]
    public Task Delete(string proxyNodeId)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Delete all proxy nodes
    /// </summary>
    [HttpDelete]
    public Task DeleteAll()
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Reset the state of the proxy nodes
    /// </summary>
    [HttpPost("reset-state")]
    public Task ResetState()
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Import proxy nodes from text content
    /// </summary>
    /// <param name="text">Plain text content containing proxy URLs (one per line or comma-separated)</param>
    [HttpPost("import")]
    public Task Import([FromBody] string text)
    {
        throw new SwaggerOnlyException();
    }
}
