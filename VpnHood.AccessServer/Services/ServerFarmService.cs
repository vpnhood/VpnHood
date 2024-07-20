using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.ServerFarms;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;
using VpnHood.AccessServer.Repos;
using VpnHood.Common;
using AccessPointView = VpnHood.AccessServer.Dtos.ServerFarms.AccessPointView;

namespace VpnHood.AccessServer.Services;

public class ServerFarmService(
    VhRepo vhRepo,
    ServerConfigureService serverConfigureService,
    CertificateService certificateService,
    CertificateValidatorService certificateValidatorService,
    HttpClient httpClient
)
{
    public async Task<ServerFarmData> Create(Guid projectId, ServerFarmCreateParams createParams)
    {
        // create default name
        createParams.ServerFarmName = createParams.ServerFarmName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.ServerFarmName))
            createParams.ServerFarmName = Resource.NewServerFarmTemplate;
        if (createParams.ServerFarmName.Contains("##")) {
            var names = await vhRepo.ServerFarmNames(projectId);
            createParams.ServerFarmName = AccessServerUtil.FindUniqueName(createParams.ServerFarmName, names);
        }

        // Set ServerProfileId
        var serverProfile = createParams.ServerProfileId != null
            ? await vhRepo.ServerProfileGet(projectId, createParams.ServerProfileId.Value)
            : await vhRepo.ServerProfileGetDefault(projectId);

        var serverFarmId = Guid.NewGuid();
        var serverFarm = new ServerFarmModel {
            ProjectId = projectId,
            ServerFarmId = serverFarmId,
            ServerProfileId = serverProfile.ServerProfileId,
            ServerFarmName = createParams.ServerFarmName,
            CreatedTime = DateTime.UtcNow,
            UseHostName = false,
            Secret = GmUtil.GenerateKey(),
            TokenJson = null,
            TokenError = null,
            TokenUrl = createParams.TokenUrl?.ToString(),
            PushTokenToClient = true,
            MaxCertificateCount = 1,
            Servers = []
        };

        FarmTokenBuilder.UpdateIfChanged(serverFarm);
        await vhRepo.AddAsync(serverFarm);
        await vhRepo.SaveChangesAsync();

        // invalidate farm cache
        await certificateService.Replace(projectId, serverFarmId, null);
        return await Get(projectId, serverFarmId, false);
    }

    public async Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary)
    {
        var dtos = includeSummary
            ? await ListWithSummary(projectId, serverFarmId: serverFarmId, includeCertificates: true)
            : await List(projectId, serverFarmId: serverFarmId, includeCertificates: true);

        var serverFarmData = dtos.Single();
        return serverFarmData;
    }

    public async Task<ValidateTokenUrlResult> ValidateTokenUrl(Guid projectId, Guid serverFarmId,
        CancellationToken cancellationToken)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);

        if (string.IsNullOrEmpty(serverFarm.TokenUrl))
            throw new InvalidOperationException(
                $"{nameof(serverFarm.TokenUrl)} has not been set."); // there is no token at the moment

        if (string.IsNullOrEmpty(serverFarm.TokenJson))
            throw new InvalidOperationException(
                "Farm has not been initialized yet."); // there is no token at the moment

        var curFarmToken = GmUtil.JsonDeserialize<ServerToken>(serverFarm.TokenJson);
        try {
            if (curFarmToken.IsValidHostName && string.IsNullOrEmpty(curFarmToken.HostName))
                throw new Exception("You farm needs a valid certificate.");

            if (!curFarmToken.IsValidHostName && GmUtil.IsNullOrEmpty(curFarmToken.HostEndPoints))
                throw new Exception("You farm needs at-least a public in token endpoint");

            // create no cache request
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, serverFarm.TokenUrl);
            httpRequestMessage.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            var responseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
            var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);

            var buf = new byte[1024 * 8]; // make sure don't fetch a big data
            var read = stream.ReadAtLeast(buf, buf.Length, false);
            var encFarmToken = Encoding.UTF8.GetString(buf, 0, read);
            var remoteFarmToken = ServerToken.Decrypt(curFarmToken.Secret!, encFarmToken);
            var isUpToDate = !remoteFarmToken.IsTokenUpdated(curFarmToken);
            return new ValidateTokenUrlResult {
                RemoteTokenTime = remoteFarmToken.CreatedTime,
                IsUpToDate = isUpToDate,
                ErrorMessage = isUpToDate ? null : "The token uploaded to the URL is old and needs to be updated."
            };
        }
        catch (Exception ex) {
            return new ValidateTokenUrlResult {
                RemoteTokenTime = null,
                IsUpToDate = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ServerFarmData> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);
        var certificate = serverFarm.GetCertificateInToken();

        var reconfigureServers = false;

        // change other properties
        if (updateParams.ServerFarmName != null) serverFarm.ServerFarmName = updateParams.ServerFarmName.Value;
        if (updateParams.UseHostName != null) serverFarm.UseHostName = updateParams.UseHostName;
        if (updateParams.PushTokenToClient != null) serverFarm.PushTokenToClient = updateParams.PushTokenToClient;
        if (updateParams.TokenUrl != null) serverFarm.TokenUrl = updateParams.TokenUrl?.ToString();

        // set secret
        if (updateParams.Secret != null && !updateParams.Secret.Value.SequenceEqual(serverFarm.Secret)) {
            serverFarm.Secret = updateParams.Secret;
            reconfigureServers = true;
        }

        // Set ServerProfileId
        if (updateParams.ServerProfileId != null && updateParams.ServerProfileId != serverFarm.ServerProfileId) {
            // makes sure that the serverProfile belongs to this project
            var serverProfile = await vhRepo.ServerProfileGet(projectId, updateParams.ServerProfileId);
            serverFarm.ServerProfileId = serverProfile.ServerProfileId;
            reconfigureServers = true;
        }

        // set certificate
        var validateCertificate = false;
        if (updateParams.AutoValidateCertificate != null &&
            updateParams.AutoValidateCertificate != certificate.AutoValidate) {
            validateCertificate = updateParams.AutoValidateCertificate.Value;
            certificate.AutoValidate = updateParams.AutoValidateCertificate.Value;
            certificate.ValidateInprogress = updateParams.AutoValidateCertificate.Value;
        }

        // set certificate history count
        if (updateParams.MaxCertificateCount != null &&
            updateParams.MaxCertificateCount != serverFarm.MaxCertificateCount) {
            if (serverFarm.MaxCertificateCount < 1)
                throw new ArgumentException("MaxCertificateCount can not be negative.",
                    nameof(serverFarm.MaxCertificateCount));

            if (serverFarm.MaxCertificateCount > 10)
                throw new ArgumentException("MaxCertificateCount can be more than 10.",
                    nameof(serverFarm.MaxCertificateCount));

            serverFarm.MaxCertificateCount = updateParams.MaxCertificateCount.Value;

            var certificates = serverFarm.Certificates!
                .Where(x => !x.IsInToken)
                .OrderByDescending(x => x.CreatedTime)
                .ToArray();

            // delete the rest
            foreach (var cert in certificates.Skip(serverFarm.MaxCertificateCount - 1))
                cert.IsDeleted = true;

            reconfigureServers = true;
        }

        // update
        await serverConfigureService.SaveChangesAndInvalidateServerFarm(projectId, serverFarmId, reconfigureServers);

        // validate certificate
        if (validateCertificate)
            _ = certificateValidatorService.ValidateJob(projectId, serverFarmId, true, CancellationToken.None);

        // update cache after save
        var ret = await Get(projectId, serverFarmId, false);
        return ret;
    }

    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null,
        Guid? serverFarmId = null, bool includeCertificates = false,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var farmViews = await vhRepo.ServerFarmListView(projectId, search: search, serverFarmId: serverFarmId,
            recordIndex: recordIndex, recordCount: recordCount);

        var accessPoints = await GetPublicInTokenAccessPoints(farmViews.Select(x => x.ServerFarm.ServerFarmId));
        var dtos = farmViews
            .Select(x => new ServerFarmData {
                ServerFarm = x.ServerFarm.ToDto(x.ServerProfileName),
                AccessPoints = accessPoints.Where(y => y.ServerFarmId == x.ServerFarm.ServerFarmId),
                Certificates = x.ServerFarm.Certificates?.Select(z => z.ToDto()).ToArray()
            });

        return dtos.ToArray();
    }

    public async Task<ServerFarmData[]> ListWithSummary(Guid projectId, string? search = null,
        Guid? serverFarmId = null, bool includeCertificates = false,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var farmViews = await vhRepo.ServerFarmListView(projectId, search: search, serverFarmId: serverFarmId,
            includeSummary: true,
            recordIndex: recordIndex, recordCount: recordCount);

        // create result
        var accessPoints = await GetPublicInTokenAccessPoints(farmViews.Select(x => x.ServerFarm.ServerFarmId));
        var now = DateTime.UtcNow;
        var ret = farmViews.Select(x => new ServerFarmData {
            ServerFarm = x.ServerFarm.ToDto(x.ServerProfileName),
            AccessPoints = accessPoints.Where(y => y.ServerFarmId == x.ServerFarm.ServerFarmId),
            Certificates = x.ServerFarm.Certificates?.Select(z => z.ToDto()).ToArray(),
            Summary = new ServerFarmSummary {
                ActiveTokenCount = x.AccessTokens!.Count(accessToken => accessToken.LastUsedTime >= now.AddDays(-7)),
                InactiveTokenCount = x.AccessTokens!.Count(accessToken => accessToken.LastUsedTime < now.AddDays(-7)),
                UnusedTokenCount = x.AccessTokens!.Count(accessToken => accessToken.FirstUsedTime == null),
                TotalTokenCount = x.AccessTokens!.Length,
                ServerCount = x.ServerCount!.Value
            }
        });

        return ret.ToArray();
    }

    private async Task<AccessPointView[]> GetPublicInTokenAccessPoints(IEnumerable<Guid> farmIds)
    {
        var farmAccessPoints = await vhRepo.AccessPointListByFarms(farmIds);
        var items = new List<AccessPointView>();

        foreach (var farmAccessPoint in farmAccessPoints)
            items.AddRange(farmAccessPoint.AccessPoints
                .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
                .Select(accessPoint => new AccessPointView {
                    ServerFarmId = farmAccessPoint.ServerFarmId,
                    ServerId = farmAccessPoint.ServerId,
                    ServerName = farmAccessPoint.ServerName,
                    TcpEndPoint = new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort)
                }));

        return items.ToArray();
    }

    public async Task Delete(Guid projectId, Guid serverFarmId)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId,
            serverFarmId: serverFarmId, includeServers: true, includeAccessTokens: true);

        if (serverFarm.Servers!.Any())
            throw new InvalidOperationException("A farm with a server can not be deleted.");

        serverFarm.IsDeleted = true;
        foreach (var accessToken in serverFarm.AccessTokens!)
            accessToken.IsDeleted = true;

        await vhRepo.SaveChangesAsync();
    }

    public async Task<string> GetEncryptedToken(Guid projectId, Guid serverFarmId)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);
        var farmToken = FarmTokenBuilder.GetRequiredServerToken(serverFarm.TokenJson);
        return farmToken.Encrypt();
    }
}