using System.Text;
using System.Text.Json;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Agent.Utils;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;
using AsyncLock = GrayMint.Common.Utils.AsyncLock;

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

    private static string FixBase64String(string base64)
    {
        base64 = base64.Trim();
        var padding = base64.Length % 4;
        if (padding > 0)
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        return base64;
    }

    private static async Task<string> SendRequestToRepo(IHttpClientFactory httpClientFactory, HttpMethod httpMethod,
        FarmTokenRepoSettings repoSettings, string encFarmToken, string? sha, CancellationToken cancellationToken)
    {
        var encFarmTokenBase64 = FixBase64String(Convert.ToBase64String(Encoding.UTF8.GetBytes(encFarmToken)));
        
        // ReSharper disable once MoveLocalFunctionAfterJumpStatement
        string ReplaceVars(string str) => str
            .Replace("{access_token}", repoSettings.AccessToken, StringComparison.OrdinalIgnoreCase)
            .Replace("{sha}", sha, StringComparison.OrdinalIgnoreCase)
            .Replace("{farm_token}", encFarmToken, StringComparison.OrdinalIgnoreCase)
            .Replace("{farm_token_base64}", encFarmTokenBase64, StringComparison.OrdinalIgnoreCase);

        // create request
        var url = ReplaceVars(repoSettings.FileUrl.AbsoluteUri);
        var requestMessage = new HttpRequestMessage {
            RequestUri = new Uri(url),
            Method = httpMethod,
        };

        // get contents type
        var contentType = repoSettings.Headers.GetValueOrDefault("Content-Type", "application/json");

        // set body
        if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put) {
            // add body
            if (!string.IsNullOrWhiteSpace(repoSettings.Body)) {
                var body = ReplaceVars(repoSettings.Body);
                requestMessage.Content = new StringContent(body, Encoding.UTF8, contentType);
            }
            // add form data
            if (repoSettings.FormData.Any()) {
                var content = new MultipartFormDataContent();
                foreach (var formItem in repoSettings.FormData)
                    content.Add(new StringContent(ReplaceVars(formItem.Value)), formItem.Key);
                
                requestMessage.Content = content;
            }
        }

        // add headers
        requestMessage.Headers.UserAgent.ParseAdd("VpnHood Access Server");
        foreach (var header in repoSettings.Headers) {
            // check is belong to content headers
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) {
                if (requestMessage.Content == null)
                    throw new InvalidOperationException($"Could not add {header.Key} when there is no content.");
                requestMessage.Content.Headers.Add(header.Key, ReplaceVars(header.Value));
            }
            else
                requestMessage.Headers.Add(header.Key, ReplaceVars(header.Value));
        }


        // send request
        using var httpClient = httpClientFactory.CreateClient(AgentOptions.FarmTokenRepoHttpClientName);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken);
        await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
        var result = await AgentUtil.ReadStringAtMostAsync(stream, 5000, Encoding.UTF8, cancellationToken);
        if (!responseMessage.IsSuccessStatusCode) {
            throw new HttpRequestException(
                $"StatusCode: {responseMessage.StatusCode}, " +
                $"Message: {result}", inner: null, statusCode: responseMessage.StatusCode);
        }

        return result;
    }

    private async Task Upload(FarmTokenRepoModel farmTokenRepo, string? farmTokenJson, CancellationToken cancellationToken)
    {
        var responseBody = "";
        try {
            logger.LogInformation("Uploading FarmToken to repo. ProjectId :{ProjectId}, FarmTokenRepoId: {FarmTokenRepoId}",
                farmTokenRepo.ProjectId, farmTokenRepo.FarmTokenRepoId);

            // get repo settings
            var repoSettings = farmTokenRepo.GetRepoSettings();
            if (repoSettings == null)
                throw new InvalidOperationException("RepoSettings is not set.");

            if (repoSettings.UploadMethod is UploadMethod.None)
                throw new InvalidOperationException("AutoPublish has been disabled.");

            if (farmTokenJson is null)
                throw new Exception("FarmToken is not ready yet");

            var farmToken = GmUtil.JsonDeserialize<ServerToken>(farmTokenJson);
            var encFarmToken = farmToken.Encrypt();

            string? sha = null;
            if (repoSettings.Body?.Contains("{sha}", StringComparison.OrdinalIgnoreCase) == true ||
                repoSettings.FormData.Any()) {
                try {
                    var fileInfo = await SendRequestToRepo(httpClientFactory, HttpMethod.Get, repoSettings, encFarmToken, null, cancellationToken);
                    var jsonDoc = JsonDocument.Parse(fileInfo);
                    if (jsonDoc.RootElement.TryGetProperty("sha", out var shaElement) && shaElement.ValueKind == JsonValueKind.String)
                        sha = shaElement.GetString();
                }
                catch (Exception ex) {
                    logger.LogWarning(ex, "Could not get FarmToken info for sha. ProjectId :{ProjectId}, FarmTokenRepoId: {FarmTokenRepoId}",
                        farmTokenRepo.ProjectId, farmTokenRepo.FarmTokenRepoId);
                }
            }

            try {
                // determine http method
                var httpMethod = repoSettings.UploadMethod switch {
                    UploadMethod.Post => HttpMethod.Post,
                    UploadMethod.Put => HttpMethod.Put,
                    UploadMethod.PutPost => HttpMethod.Put,
                    _ => throw new InvalidOperationException("Invalid UploadMethod")
                };

                responseBody = await SendRequestToRepo(httpClientFactory, httpMethod, repoSettings, encFarmToken, sha, cancellationToken);
            }
            catch (HttpRequestException ex) when (repoSettings.UploadMethod == UploadMethod.PutPost ) {
                logger.LogInformation(ex,
                    "Try POST instead of PUT to upload FarmToken as. ProjectId :{ProjectId}, FarmTokenRepoId: {FarmTokenRepoId}.",
                    farmTokenRepo.ProjectId, farmTokenRepo.FarmTokenRepoId);

                responseBody = await SendRequestToRepo(httpClientFactory, HttpMethod.Post, repoSettings, encFarmToken, sha, cancellationToken);
            }
            farmTokenRepo.Error = null;
        }
        catch (Exception ex) {
            farmTokenRepo.Error = $"Upload Failed. {ex.Message}";
            if (!string.IsNullOrEmpty(responseBody))
                farmTokenRepo.Error += $"Upload Error. {responseBody}";

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
