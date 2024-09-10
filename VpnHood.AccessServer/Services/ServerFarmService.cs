using System.Net;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.ServerFarms;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Repos;
using AccessPointView = VpnHood.AccessServer.Dtos.ServerFarms.AccessPointView;

namespace VpnHood.AccessServer.Services;

public class ServerFarmService(
    VhRepo vhRepo,
    ServerConfigureService serverConfigureService,
    CertificateService certificateService,
    CertificateValidatorService certificateValidatorService)
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
            PushTokenToClient = true,
            MaxCertificateCount = 1
        };

        await vhRepo.AddAsync(serverFarm);
        await vhRepo.SaveChangesAsync();

        // invalidate farm cache
        await certificateService.Replace(projectId, serverFarmId, null);
        return await Get(projectId, serverFarmId, false);
    }

    public async Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary)
    {
        var dtos = includeSummary
            ? await ListWithSummary(projectId, serverFarmId: serverFarmId)
            : await List(projectId, serverFarmId: serverFarmId);

        var certificateModel = await vhRepo.ServerFarmGetInTokenCertificate(projectId, serverFarmId);
        var serverFarmData = dtos.Single();
        serverFarmData.ServerFarm.Certificate = certificateModel.ToDto();
        serverFarmData.FarmTokenRepoNames = (await vhRepo.FarmTokenRepoListNames(projectId, serverFarmId)).ToArray();
        return serverFarmData;
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
            if (updateParams.MaxCertificateCount < 1 || updateParams.MaxCertificateCount > 10)
                throw new ArgumentException("MaxCertificateCount must be between 1 and 10.",
                    nameof(updateParams.MaxCertificateCount));

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
        Guid? serverFarmId = null,
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
        Guid? serverFarmId = null,
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
        var farmToken = serverFarm.GetRequiredServerToken();
        return farmToken.Encrypt(serverFarm.TokenIv);
    }
}