using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/app")]
public class AppController : ControllerBase, IAppController
{
    [HttpPatch("configure")]
    public Task<AppData> Configure(ConfigParams configParams, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("config")]
    public Task<AppData> GetConfig(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("ip-filters")]
    public Task<IpFilters> GetIpFilters(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut("ip-filters")]
    public Task SetIpFilters(IpFilters ipFilters, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("state")]
    public Task<AppState> GetState(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("connect")]
    public Task Connect(Guid? clientProfileId = null, string? serverLocation = null, 
        ConnectPlanId planId = ConnectPlanId.Normal, CancellationToken cancellationToken = default)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("diagnose")]
    public Task Diagnose(Guid? clientProfileId = null, string? serverLocation = null, 
        ConnectPlanId planId = ConnectPlanId.Normal, CancellationToken cancellationToken = default)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("disconnect")]
    public Task Disconnect(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("clear-last-error")]
    public Task ClearLastError(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("extend-by-rewarded-ad")]
    public Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut("user-settings")]
    public Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("log.txt")]
    [Produces(MediaTypeNames.Text.Plain)]
    public Task<string> Log(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("promotion.jpg")]
    [Produces(MediaTypeNames.Image.Jpeg)]
    public Task<byte[]> PromotionImage(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("installed-apps")]
    public Task<DeviceAppInfo[]> GetInstalledApps(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("version-check")]
    public Task VersionCheck(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("version-check-postpone")]
    public Task VersionCheckPostpone(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("process-types")]
    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode,
        CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("user-review")]
    public Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("countries")]
    public Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("supported-split-by-countries")]
    public Task<CountryInfo[]> GetSupportedSplitByCountries(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("internal-ad/dismiss")]
    public Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("internal-ad/error")]
    public Task InternalAdError(string errorMessage, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("remove-premium")]
    public Task RemovePremium(Guid profileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}