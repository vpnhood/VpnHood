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
        var regionData = await regionClient.CreateAsync(testApp.ProjectId, createParams);

        //-----------
        // check: get
        //-----------
        regionData = await regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId);
        Assert.AreEqual(createParams.CountryCode, regionData.Region.CountryCode);

        //-----------
        // check: update
        //-----------
        var updateParams = new RegionUpdateParams
        {
            RegionName = new PatchOfString{ Value = Guid.NewGuid().ToString() },
            CountryCode = new PatchOfString {Value = "UK" }
        };
        await regionClient.UpdateAsync(testApp.ProjectId, regionData.Region.RegionId, updateParams);
        regionData = await regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId);
        Assert.AreEqual(updateParams.RegionName.Value, regionData.Region.RegionName);
        Assert.AreEqual(updateParams.CountryCode.Value, regionData.Region.CountryCode);

        //-----------
        // check: delete
        //-----------
        await regionClient.DeleteAsync(testApp.ProjectId, regionData.Region.RegionId);
        await VhTestUtil.AssertNotExistsException(regionClient.GetAsync(testApp.ProjectId, regionData.Region.RegionId));
    }
}