using GrayMint.Common.Utils;
using System.Net.Http.Headers;
using System.Text;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.FarmTokenRepos;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Repos;
using VpnHood.Common;

namespace VpnHood.AccessServer.Services;


public class FarmTokenRepoService(VhRepo vhRepo, HttpClient httpClient)
{
    public async Task<FarmTokenRepo> Create(Guid projectId, Guid serverFarmId, FarmTokenRepoCreateParams createParams)
    {
        // make sure serverFamId belong to project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);

        var model = new FarmTokenRepoModel {
            AuthorizationKey = createParams.AuthorizationKey,
            AuthorizationValue = createParams.AuthorizationValue,
            HttpMethod = createParams.HttpMethod,
            PublishUrl = createParams.PublicUrl,
            UploadUrl = createParams.UploadUrl,
            FarmTokenRepoName = createParams.RepoName,
            ServerFarmId = serverFarm.ServerFarmId,
            ProjectId = projectId,
            FarmTokenRepoId = Guid.NewGuid(),
            Error = null,
            UploadedTime = null
        };

        var entity = await vhRepo.AddAsync(model);
        await vhRepo.SaveChangesAsync();
        return entity.ToDto();
    }

    public async Task<FarmTokenRepo> Get(Guid projectId, Guid serverFarmId, Guid farmTokenRepoId, 
        bool checkStatus, CancellationToken cancellationToken)
    {
        var farmTokenRepos = await List(projectId, serverFarmId: serverFarmId, checkStatus: checkStatus, farmTokenRepoId: farmTokenRepoId,
            cancellationToken: cancellationToken);
        return farmTokenRepos.Single();
    }

    public async Task<FarmTokenRepo[]> List(Guid projectId, Guid serverFarmId, bool checkStatus, 
        Guid? farmTokenRepoId = null, CancellationToken cancellationToken = default)
    {
        var models = farmTokenRepoId != null
            ? [await vhRepo.FarmTokenRepoGet(projectId, serverFarmId: serverFarmId, farmTokenRepoId: farmTokenRepoId.Value)]
            : await vhRepo.FarmTokenRepoList(projectId, serverFarmId);
            
        var farmTokenRepos = models.Select(x => x.ToDto()).ToArray();

        if (!checkStatus)
            return farmTokenRepos;

        // check status
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);
        var tasks = farmTokenRepos
            .Select(async x => new {
                FarmTokenRepo = x,
                ValidateTokenUrlResult = await CheckStatus(x, serverFarm.TokenJson, cancellationToken)
            })
            .ToArray();
        
        await Task.WhenAll(tasks);

        foreach (var task in tasks) {
            var result = await task;
            result.FarmTokenRepo.IsUpToDate = result.ValidateTokenUrlResult.IsUpToDate;
            result.FarmTokenRepo.Error ??= result.ValidateTokenUrlResult.ErrorMessage;
        }
        
        return farmTokenRepos;
    }

    public async Task<FarmTokenRepo> Update(Guid projectId, Guid serverFarmId, Guid farmTokenRepoId, FarmTokenRepoUpdateParams updateParams)
    {
        // make sure serverFamId belong to project
        var farmTokenRepo = await vhRepo.FarmTokenRepoGet(projectId, serverFarmId, farmTokenRepoId);

        if (updateParams.AuthorizationKey != null) farmTokenRepo.AuthorizationKey = updateParams.AuthorizationKey;
        if (updateParams.AuthorizationValue != null) farmTokenRepo.AuthorizationValue = updateParams.AuthorizationValue;
        if (updateParams.HttpMethod != null) farmTokenRepo.HttpMethod = updateParams.HttpMethod;
        if (updateParams.PublishUrl != null) farmTokenRepo.PublishUrl = updateParams.PublishUrl;
        if (updateParams.UploadUrl != null) farmTokenRepo.UploadUrl = updateParams.UploadUrl;
        if (updateParams.RepoName != null) farmTokenRepo.FarmTokenRepoName = updateParams.RepoName;
        await vhRepo.SaveChangesAsync();
        return farmTokenRepo.ToDto();
    }

    public Task Delete(Guid projectId, Guid serverFarmId, string farmTokenRepoId)
    {
        return vhRepo.FarmTokenRepoDelete(projectId, serverFarmId, Guid.Parse(farmTokenRepoId));
    }

    private async Task<ValidateTokenUrlResult> CheckStatus(FarmTokenRepo farmTokenRepo, string? farmTokenJson, CancellationToken cancellationToken)
    {
        try {
            if (farmTokenRepo.PublishUrl == null)
                throw new InvalidOperationException(
                    $"{nameof(farmTokenRepo.PublishUrl)} has not been set."); // there is no token at the moment

            if (string.IsNullOrEmpty(farmTokenJson))
                throw new InvalidOperationException(
                    "Farm has not been initialized yet."); // there is no token at the moment

            var curFarmToken = GmUtil.JsonDeserialize<ServerToken>(farmTokenJson);

            if (curFarmToken.IsValidHostName && string.IsNullOrEmpty(curFarmToken.HostName))
                throw new Exception("You farm needs a valid certificate.");

            if (!curFarmToken.IsValidHostName && GmUtil.IsNullOrEmpty(curFarmToken.HostEndPoints))
                throw new Exception("You farm needs at-least a public in token endpoint");

            // create no cache request
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, farmTokenRepo.PublishUrl);
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

    public async Task<FarmTokenRepoSummary> GetSummary(Guid projectId, Guid serverFarmId, CancellationToken cancellationToken)
    {
        var farmTokenRepos = await List(projectId, serverFarmId: serverFarmId, checkStatus: true, cancellationToken: cancellationToken);
        return new FarmTokenRepoSummary {
            UpToDateRepoNames = farmTokenRepos
                .Where(x => x.IsUpToDate is true)
                .Select(x => x.FarmTokenRepoName)
                .ToArray(),
            OutdatedRepoNames = farmTokenRepos
                .Where(x => x.IsUpToDate is null or false)
                .Select(x => x.FarmTokenRepoName)
                .ToArray(),
            Total = farmTokenRepos.Length
        };
    }
}