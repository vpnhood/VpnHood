using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
// ReSharper disable InvalidXmlDocComment

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/proxy-endpoints")]
public class ProxyEndPointController : ControllerBase, IProxyEndPointController
{
    /// <summary>
    /// Get the device information
    /// </summary>
    [HttpGet("device")]
    public Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// List all proxy endpoints
    /// </summary>
    [HttpGet]
    public Task<AppProxyEndPointInfo[]> List(
        [FromQuery] bool includeSucceeded = true,
        [FromQuery] bool includeFailed = true,
        [FromQuery] bool includeUnknown = true,
        [FromQuery] bool includeDisabled = true,
        [FromQuery] int? recordIndex = null,
        [FromQuery] int? recordCount = null,
        CancellationToken cancellationToken = default)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Get a proxy endpoint by id
    /// </summary>
    [HttpGet("{proxyEndPointId}")]
    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Parse and create a proxy endpoint from the provided text
    /// </summary>
    /// <param name="text">The text to parse</param>
    /// <param name="defaults">The default settings for the proxy endpoint</param>
    [HttpPost("parse")]
    public Task<AppProxyEndPointInfo> Parse([FromQuery] string text, 
        [FromBody] ProxyEndPointDefaults defaults,
        CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Update an existing proxy endpoint
    /// </summary>
    /// <param name="proxyEndPointId">The ID of the proxy endpoint to update</param>
    /// <param name="proxyEndPoint">The updated proxy endpoint data</param>
    [HttpPut("{proxyEndPointId}")]
    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, [FromBody] ProxyEndPoint proxyEndPoint,
        CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Add a new proxy endpoint
    /// </summary>
    /// <param name="proxyEndPoint">The proxy endpoint to add</param>
    [HttpPost]
    public Task<AppProxyEndPointInfo> Add([FromBody] ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Delete a proxy endpoint by its ID
    /// </summary>
    /// <param name="proxyEndPointId">The ID of the proxy endpoint to delete</param>
    [HttpDelete("{proxyEndPointId}")]
    public Task Delete(string proxyEndPointId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Delete all proxy endpoints
    /// </summary>
    [HttpDelete]
    public Task DeleteAll(
        [FromQuery] bool deleteSucceeded,
        [FromQuery] bool deleteFailed,
        [FromQuery] bool deleteUnknown,
        [FromQuery] bool deleteDisabled,
        CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Reset the state of the proxy endpoints
    /// </summary>
    [HttpPost("reset-states")]
    public Task ResetStates(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Import proxy endpoints from text content
    /// </summary>
    /// <param name="content">Plain text content containing proxy URLs (one per line or comma-separated)</param>
    [HttpPost("import")]
    public Task Import([FromBody] string content, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Reload proxy endpoints from configured URL
    /// </summary>
    [HttpPost("reload-url")]
    public Task ReloadUrl(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    /// <summary>
    /// Disable all failed proxy endpoints
    /// </summary>
    [HttpPost("disable-failed")]
    public Task DisableAllFailed(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}