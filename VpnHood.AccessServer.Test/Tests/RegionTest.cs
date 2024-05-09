using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class RegionTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testApp = await TestApp.Create();
        var regionClient = testApp.RegionsClient;

        //-----------
        // check: Create
        //-----------
        var createParams = new RegionCreateParams
        {
            RegionName = Guid.NewGuid().ToString(),
            CountryCode = "us"
        };
        var region = await regionClient.CreateAsync(testApp.ProjectId, createParams);

        //-----------
        // check: get
        //-----------
        region = await regionClient.GetAsync(testApp.ProjectId, region.RegionId);
        Assert.AreEqual(createParams.CountryCode, region.CountryCode);

        //-----------
        // check: update
        //-----------
        var updateParams = new RegionUpdateParams
        {
            RegionName = new PatchOfString{ Value = Guid.NewGuid().ToString() },
            CountryCode = new PatchOfString {Value = "UK" }
        };
        await regionClient.UpdateAsync(testApp.ProjectId, region.RegionId, updateParams);
        region = await regionClient.GetAsync(testApp.ProjectId, region.RegionId);
        Assert.AreEqual(updateParams.RegionName.Value, region.RegionName);
        Assert.AreEqual(updateParams.CountryCode.Value, region.CountryCode);

        //-----------
        // check: delete
        //-----------
        await regionClient.DeleteAsync(testApp.ProjectId, region.RegionId);
        await VhTestUtil.AssertNotExistsException(regionClient.GetAsync(testApp.ProjectId, region.RegionId));
    }
}