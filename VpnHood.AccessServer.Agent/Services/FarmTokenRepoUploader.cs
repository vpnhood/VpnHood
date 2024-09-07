using System.Text;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Agent.Services;

public class FarmTokenRepoUploader(
    VhAgentRepo vhAgentRepo,
    IHttpClientFactory httpClientFactory,
    ILogger<FarmTokenRepoUploader> logger)
    : IGrayMintJob
{
    public static async Task UploadPendingTokensJob(IServiceScopeFactory serviceScopeFactory, CancellationToken cancellationToken)
    {
        // create new scope
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var farmTokenRepoUploader = scope.ServiceProvider.GetRequiredService<FarmTokenRepoUploader>();
        await farmTokenRepoUploader.UploadPendingTokens(true, cancellationToken);
    }

    // upload all outdated tokens
    public async Task UploadPendingTokens(bool saveChanges, CancellationToken cancellationToken)
    {
        using var asyncLock = await AsyncLock.LockAsync("FarmTokens.UploadPendingTokens");
        logger.LogInformation("Start uploading FarmToken to repos...");

        var farmTokenRepos = await vhAgentRepo.FarmTokenRepoListPendingUpload();
        var tasks = farmTokenRepos
            .Select(async x => {
                await Upload(x, x.ServerFarm!.TokenJson, cancellationToken);
                return x;
            })
            .ToArray();

        await Task.WhenAll(tasks);

        if (saveChanges)
            await vhAgentRepo.SaveChangesAsync();
    }

    private async Task Upload(FarmTokenRepoModel farmTokenRepo, string? farmTokenJson, CancellationToken cancellationToken)
    {
        try {
            logger.LogInformation("Uploading FarmToken to repo. ProjectId :{ProjectId}, FarmTokenRepoId: {FarmTokenRepoId}",
                farmTokenRepo.ProjectId, farmTokenRepo.FarmTokenRepoId);

            // get repo settings
            var repoSettings = farmTokenRepo.GetRepoSettings();
            if (repoSettings == null)
                throw new Exception("RepoSettings is not set.");

            if (farmTokenJson is null)
                throw new Exception("FarmToken is not ready yet");

            var farmToken = GmUtil.JsonDeserialize<ServerToken>(farmTokenJson);
            var encFarmToken = farmToken.Encrypt();

            // body
            var body = repoSettings.Body ?? encFarmToken;
            body  = body.Replace("{content}", encFarmToken);

            // create request
            var requestMessage = new HttpRequestMessage {
                RequestUri = repoSettings.FileUrl,
                Content = new StringContent(body, Encoding.UTF8),
                Method = HttpMethod.Parse(repoSettings.UploadMethod),
            };

            // add authorization header
            foreach (var header in repoSettings.Headers)
                requestMessage.Headers.Add(header.Key, header.Value);

            // send request
            using var httpClient = httpClientFactory.CreateClient(AgentOptions.FarmTokenRepoHttpClientName);
            var response = await httpClient.SendAsync(requestMessage, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) {
            farmTokenRepo.Error = ex.Message;
            logger.LogError(ex, "Could not upload FarmToken to repo. ProjectId :{ProjectId}, FarmTokenRepoId: {FarmTokenRepoId}",
                farmTokenRepo.ProjectId, farmTokenRepo.FarmTokenRepoId);
        }
        finally {
            farmTokenRepo.IsPendingUpload = false;
            farmTokenRepo.UploadedTime = DateTime.UtcNow;
        }
    }

    public Task RunJob(CancellationToken cancellationToken)
    {
        return UploadPendingTokens(saveChanges: true, cancellationToken);
    }
}
