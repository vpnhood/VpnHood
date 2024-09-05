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
            PublishUrl = new Uri("http://127.0.0.1:6090/file"),
            UploadUrl = new Uri("http://127.0.0.1:6090/upload"),
            HttpMethod = "PUT",
            AuthorizationKey = Guid.NewGuid().ToString(),
            AuthorizationValue = Guid.NewGuid().ToString(),
            RepoName = Guid.NewGuid().ToString()
        };

        // assert get
        var tokenRepo =
            await farm.TestApp.FarmTokenReposClient.CreateAsync(farm.ProjectId, farm.ServerFarmId, createParams);
        tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
            tokenRepo.FarmTokenRepoId);
        Assert.AreEqual(createParams.PublishUrl, tokenRepo.PublishUrl);
        Assert.AreEqual(createParams.UploadUrl, tokenRepo.UploadUrl);
        Assert.AreEqual(createParams.HttpMethod, tokenRepo.HttpMethod);
        Assert.AreEqual(createParams.AuthorizationKey, tokenRepo.AuthorizationKey);
        Assert.AreEqual(createParams.AuthorizationValue, tokenRepo.AuthorizationValue);
        Assert.AreEqual(createParams.RepoName, tokenRepo.FarmTokenRepoName);
        Assert.IsNull(tokenRepo.IsUpToDate);
        Assert.IsNull(tokenRepo.Error);

        // assert update
        var patchParams = new FarmTokenRepoUpdateParams {
            AuthorizationKey = new PatchOfString { Value = Guid.NewGuid().ToString() },
            AuthorizationValue = new PatchOfString { Value = Guid.NewGuid().ToString() },
            HttpMethod = new PatchOfString { Value = "POST" },
            PublishUrl = new PatchOfUri { Value = new Uri("http://127.0.0.1:6091/updated2") },
            UploadUrl = new PatchOfUri { Value = new Uri("http://127.0.0.1:6091/upload/updated2") },
            RepoName = new PatchOfString { Value = Guid.NewGuid().ToString() }
        };
        tokenRepo = await farm.TestApp.FarmTokenReposClient.UpdateAsync(farm.ProjectId, farm.ServerFarmId,
            tokenRepo.FarmTokenRepoId, patchParams);
        Assert.AreEqual(patchParams.PublishUrl.Value, tokenRepo.PublishUrl);
        Assert.AreEqual(patchParams.UploadUrl.Value, tokenRepo.UploadUrl);
        Assert.AreEqual(patchParams.HttpMethod.Value, tokenRepo.HttpMethod);
        Assert.AreEqual(patchParams.AuthorizationKey.Value, tokenRepo.AuthorizationKey);
        Assert.AreEqual(patchParams.AuthorizationValue.Value, tokenRepo.AuthorizationValue);
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

        // create farm
        var createParams = new FarmTokenRepoCreateParams {
            PublishUrl = fileUrl1,
            UploadUrl = fileUrl1,
            HttpMethod = "PUT",
            AuthorizationKey = fileServer1.AuthorizationKey,
            AuthorizationValue = fileServer1.AuthorizationValue,
            RepoName = Guid.NewGuid().ToString()
        };

        // assert get
        var tokenRepo = await farm.TestApp.FarmTokenReposClient.CreateAsync(farm.ProjectId, farm.ServerFarmId, createParams);

        await VhTestUtil.AssertEqualsWait(true, async () => {
            tokenRepo = await farm.TestApp.FarmTokenReposClient.GetAsync(farm.ProjectId, farm.ServerFarmId,
                tokenRepo.FarmTokenRepoId, checkStatus: true);
            return tokenRepo.IsUpToDate == true;
        });

        Assert.IsNull(tokenRepo.Error);

        // Check after patch
        using var fileServer2 = new TestFileServer($"Authorization_{Guid.NewGuid()}", $"bearer key_{Guid.NewGuid()}");
        var fileUrl2 = new Uri(fileServer2.ApiUrl, $"f3_{Guid.NewGuid()}");
        var updateParams = new FarmTokenRepoUpdateParams {
            PublishUrl = new PatchOfUri { Value = fileUrl2 },
            UploadUrl = new PatchOfUri { Value = fileUrl2 },
            HttpMethod = new PatchOfString { Value = "POST" },
            AuthorizationKey = new PatchOfString { Value = fileServer2.AuthorizationKey },
            AuthorizationValue = new PatchOfString { Value = fileServer2.AuthorizationValue },
            RepoName = new PatchOfString { Value = Guid.NewGuid().ToString() },
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