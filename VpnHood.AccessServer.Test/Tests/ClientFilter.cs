using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ClientFilterTest
{
    [TestMethod]
    public async Task Crud()
    {
        using var testApp = await TestApp.Create(isFree: false);

        // create
        await testApp.ClientFiltersClient.CreateAsync(testApp.ProjectId, new ClientFilterCreateParams {
            ClientFilterName = Guid.NewGuid().ToString(),
            Filter = "#premium || (#loc:us && #loc:fa)"
        });

        // create
        var createParams = new ClientFilterCreateParams {
            Filter = "#premium || (#loc:us || #loc:ca)",
            ClientFilterName = Guid.NewGuid().ToString()
        };
        var clientFilter = await testApp.ClientFiltersClient.CreateAsync(testApp.ProjectId, createParams);
        Assert.AreEqual(clientFilter.ClientFilterName, createParams.ClientFilterName);

        // get
        clientFilter = await testApp.ClientFiltersClient.GetAsync(testApp.ProjectId, clientFilter.ClientFilterId);
        Assert.AreEqual(clientFilter.ClientFilterName, createParams.ClientFilterName);

        // update
        var updateParams = new ClientFilterUpdateParams {
            ClientFilterName = new PatchOfString { Value = Guid.NewGuid().ToString() },
            Filter = new PatchOfString { Value = "#premium" }
        };
        clientFilter = await testApp.ClientFiltersClient.UpdateAsync(testApp.ProjectId, clientFilter.ClientFilterId, updateParams);
        Assert.AreEqual(clientFilter.ClientFilterName, updateParams.ClientFilterName.Value);
        Assert.AreEqual(clientFilter.Filter, updateParams.Filter?.Value);

        // delete
        await testApp.ClientFiltersClient.DeleteAsync(testApp.ProjectId, clientFilter.ClientFilterId);
        await VhTestUtil.AssertNotExistsException(testApp.ClientFiltersClient.GetAsync(testApp.ProjectId, clientFilter.ClientFilterId));
    }
}