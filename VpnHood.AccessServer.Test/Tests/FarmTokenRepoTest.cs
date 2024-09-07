using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.AccessServer.Test.Helper;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class FarmTokenRepoTest
{
    [TestMethod]
    public async Task Crud()
    {
        // create farm
        var farm = await ServerFarmDom.Create();
        var createParams = new FarmTokenRepoCreateParams {
            RepoName = Guid.NewGuid().ToString(),
            PublishUrl = new Uri("http://127.0.0.1:6090/file"),
            RepoSettings = new FarmTokenRepoSettings {
                UploadMethod = "PUT",
                FileUrl = new Uri("http://127.0.0.1:6090/upload"),
                Headers = new Dictionary<string, string> {
                    {Guid.NewGuid().ToString(), Guid.NewGuid().ToString()}
                }

            }
        };

        // assert get
        var tokenRepo =
            await farm.TestApp.FarmTokenReposClient.CreateAsync(farm.ProjectId, farm.ServerFarmId, createParams);
        tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
            tokenRepo.FarmTokenRepoId);
        Assert.AreEqual(createParams.PublishUrl, tokenRepo.PublishUrl);
        Assert.AreEqual(JsonSerializer.Serialize(createParams.RepoSettings), JsonSerializer.Serialize(tokenRepo.RepoSettings));
        Assert.AreEqual(createParams.RepoName, tokenRepo.FarmTokenRepoName);
        Assert.IsNull(tokenRepo.IsUpToDate);
        Assert.IsNull(tokenRepo.Error);

        // assert update
        var patchParams = new FarmTokenRepoUpdateParams {
            PublishUrl = new PatchOfUri { Value = new Uri("http://127.0.0.1:6091/updated2") },
            RepoName = new PatchOfString { Value = Guid.NewGuid().ToString() },
            RepoSettings = new PatchOfFarmTokenRepoSettings {
                Value = new FarmTokenRepoSettings {
                    FileUrl = new Uri("http://127.0.0.1:6091/upload/updated2"),
                    UploadMethod = "POST",
                    Headers = new Dictionary<string, string> {
                        {Guid.NewGuid().ToString(), Guid.NewGuid().ToString()}
                    }
                }
            }
        };
        tokenRepo = await farm.TestApp.FarmTokenReposClient.UpdateAsync(farm.ProjectId, farm.ServerFarmId,
            tokenRepo.FarmTokenRepoId, patchParams);
        Assert.AreEqual(patchParams.PublishUrl.Value, tokenRepo.PublishUrl);
        Assert.AreEqual(JsonSerializer.Serialize(patchParams.RepoSettings.Value), JsonSerializer.Serialize(tokenRepo.RepoSettings));
        Assert.AreEqual(patchParams.RepoName.Value, tokenRepo.FarmTokenRepoName);
        Assert.IsNull(tokenRepo.IsUpToDate);
        Assert.IsNull(tokenRepo.Error);

        await VhTestUtil.AssertEqualsWait(true, async () => {
            tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
                tokenRepo.FarmTokenRepoId, checkStatus: false);
            return !string.IsNullOrEmpty(tokenRepo.Error);
        });
    }

    [TestMethod]
    public async Task AutoUpload_create_And_update()
    {
        var farm = await ServerFarmDom.Create();

        using var fileServer1 = new TestFileServer("CustomAuthorization", $"bearer key_{Guid.NewGuid()}");
        var fileUrl1 = new Uri(fileServer1.ApiUrl, $"f3_{Guid.NewGuid()}");
        ArgumentNullException.ThrowIfNull(fileServer1.AuthorizationKey);
        ArgumentNullException.ThrowIfNull(fileServer1.AuthorizationValue);

        // create farm token
        var createParams = new FarmTokenRepoCreateParams {
            PublishUrl = fileUrl1,
            RepoName = Guid.NewGuid().ToString(),
            RepoSettings = new FarmTokenRepoSettings
            {
                FileUrl = fileUrl1,
                UploadMethod = "PUT",
                Headers = new Dictionary<string, string> {
                    {fileServer1.AuthorizationKey, fileServer1.AuthorizationValue}
                },
                Body = "{content}"
            }
        };

        var tokenRepo = await farm.TestApp.FarmTokenReposClient.CreateAsync(farm.ProjectId, farm.ServerFarmId, createParams);

        // assert get
        await VhTestUtil.AssertEqualsWait(true, async () => {
            tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
                tokenRepo.FarmTokenRepoId, checkStatus: true);
            return tokenRepo.IsUpToDate == true;
        });

        Assert.IsNull(tokenRepo.Error);

        // Check after patch
        using var fileServer2 = new TestFileServer($"Authorization_{Guid.NewGuid()}", $"bearer key_{Guid.NewGuid()}");
        var fileUrl2 = new Uri(fileServer2.ApiUrl, $"f3_{Guid.NewGuid()}");
        ArgumentNullException.ThrowIfNull(fileServer2.AuthorizationKey);
        ArgumentNullException.ThrowIfNull(fileServer2.AuthorizationValue);

        var updateParams = new FarmTokenRepoUpdateParams {
            RepoName = new PatchOfString { Value = Guid.NewGuid().ToString() },
            PublishUrl = new PatchOfUri { Value = fileUrl2 },
            RepoSettings = new PatchOfFarmTokenRepoSettings {
                Value = new FarmTokenRepoSettings {
                    FileUrl = fileUrl2,
                    UploadMethod = "POST",
                    Headers = new Dictionary<string, string> {
                        {fileServer2.AuthorizationKey, fileServer2.AuthorizationValue},
                    },
                    Body = "{content}"
                },
            },
        };
        await farm.TestApp.FarmTokenReposClient.UpdateAsync(farm.ProjectId, farm.ServerFarmId, tokenRepo.FarmTokenRepoId, updateParams);

        // assert get
        await VhTestUtil.AssertEqualsWait(true, async () => {
            tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
                tokenRepo.FarmTokenRepoId, checkStatus: true);
            return tokenRepo.IsUpToDate == true && tokenRepo.PublishUrl == updateParams.PublishUrl.Value;
        });

        Assert.IsNull(tokenRepo.Error);
    }
}

// simple file repo using EmbedIO