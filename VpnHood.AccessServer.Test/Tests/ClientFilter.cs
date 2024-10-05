using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
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
            Filter = "#premium || (#loc:us && #loc:fa)",
            Description = Guid.NewGuid().ToString()
        });

        // create
        var createParams = new ClientFilterCreateParams {
            Filter = "#premium || (#loc:us || #loc:ca)",
            ClientFilterName = Guid.NewGuid().ToString(),
            Description = Guid.NewGuid().ToString()
        };
        var clientFilter = await testApp.ClientFiltersClient.CreateAsync(testApp.ProjectId, createParams);
        Assert.AreEqual(clientFilter.ClientFilterName, createParams.ClientFilterName);
        Assert.AreEqual(clientFilter.Filter, createParams.Filter);
        Assert.AreEqual(clientFilter.Description, createParams.Description);

        // get
        clientFilter = await testApp.ClientFiltersClient.GetAsync(testApp.ProjectId, clientFilter.ClientFilterId);
        Assert.AreEqual(clientFilter.ClientFilterName, createParams.ClientFilterName);
        Assert.AreEqual(clientFilter.Filter, createParams.Filter);
        Assert.AreEqual(clientFilter.Description, createParams.Description);

        // update
        var updateParams = new ClientFilterUpdateParams {
            ClientFilterName = new PatchOfString { Value = Guid.NewGuid().ToString() },
            Filter = new PatchOfString { Value = "#premium" },
            Description = new PatchOfString { Value = Guid.NewGuid().ToString() }
        };
        clientFilter = await testApp.ClientFiltersClient.UpdateAsync(testApp.ProjectId, clientFilter.ClientFilterId, updateParams);
        Assert.AreEqual(clientFilter.ClientFilterName, updateParams.ClientFilterName.Value);
        Assert.AreEqual(clientFilter.Filter, updateParams.Filter?.Value);
        Assert.AreEqual(clientFilter.Description, updateParams.Description?.Value);

        // list
        var clientFilters = await testApp.ClientFiltersClient.ListAsync(testApp.ProjectId);
        Assert.AreEqual(clientFilters.Count, 2);
        Assert.IsTrue(clientFilters.Any(x => x.ClientFilterName == clientFilter.ClientFilterName));

        // delete
        await testApp.ClientFiltersClient.DeleteAsync(testApp.ProjectId, clientFilter.ClientFilterId);
        await VhTestUtil.AssertNotExistsException(testApp.ClientFiltersClient.GetAsync(testApp.ProjectId, clientFilter.ClientFilterId));
    }

    [TestMethod]
    public async Task AssignToServer()
    {
        var testApp = await TestApp.Create(isFree: false);
        var farm = await ServerFarmDom.Create(testApp, serverCount: 0);

        // add a ClientFilter
        var clientFilter1 = await farm.TestApp.ClientFiltersClient.CreateAsync(farm.ProjectId, new ClientFilterCreateParams {
            ClientFilterName = Guid.NewGuid().ToString(),
            Filter = "#premium || (#tag1 && #tag2)",
            Description = Guid.NewGuid().ToString()
        });

        var clientFilter2 = await farm.TestApp.ClientFiltersClient.CreateAsync(farm.ProjectId, new ClientFilterCreateParams {
            ClientFilterName = Guid.NewGuid().ToString(),
            Filter = "#premium || (#tag3 && #tag4)",
            Description = Guid.NewGuid().ToString()
        });


        // add a server
        var server = await farm.AddNewServer(new ServerCreateParams {
            ClientFilterId = clientFilter1.ClientFilterId
        });

        await server.Reload();
        Assert.AreEqual(server.Server.ClientFilterId, clientFilter1.ClientFilterId);

        // reassign ClientFilter
        await server.Update(new ServerUpdateParams {
            ClientFilterId = new PatchOfString { Value = clientFilter2.ClientFilterId }
        });

        await server.Reload();
        Assert.AreEqual(server.Server.ClientFilterId, clientFilter2.ClientFilterId);
    }

    [TestMethod]
    public async Task Server_should_selected_by_client_filter()
    {
        var testApp = await TestApp.Create(isFree: false);
        var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        farm.TestApp.AgentTestApp.AgentOptions.AllowRedirect = true;

        // add a ClientFilter
        var clientFilter1 = await farm.TestApp.ClientFiltersClient.CreateAsync(farm.ProjectId, new ClientFilterCreateParams {
            ClientFilterName = Guid.NewGuid().ToString(),
            Filter = "#foo || (#tag1 || #tag2)",
            Description = Guid.NewGuid().ToString()
        });

        var clientFilter2 = await farm.TestApp.ClientFiltersClient.CreateAsync(farm.ProjectId, new ClientFilterCreateParams {
            ClientFilterName = Guid.NewGuid().ToString(),
            Filter = "#foo || (#tag3 || #tag4)",
            Description = Guid.NewGuid().ToString()
        });

        // add servers
        var server1 = await farm.AddNewServer(tags: ["#p1", "#p2"]);
        await server1.Update(new ServerUpdateParams {
            ClientFilterId = new PatchOfString { Value = clientFilter1.ClientFilterId }
        });

        var server2 = await farm.AddNewServer(tags: ["#p3", "#p4"]);
        await server2.Update(new ServerUpdateParams {
            ClientFilterId = new PatchOfString { Value = clientFilter2.ClientFilterId }
        });

        // create tokens with no access
        var accessToken = await farm.CreateAccessToken(new AccessTokenCreateParams {
            Tags = ["tag_no_where"]
        });
        await VhTestUtil.AssertApiException<SessionExceptionEx>(
            task: accessToken.CreateSession(autoRedirect: true),
            contains: "Could not find any available");

        // create tokens for server 2
        accessToken = await farm.CreateAccessToken(new AccessTokenCreateParams {
            Tags = ["#tag3"]
        });
        var session = await accessToken.CreateSession(autoRedirect: true);
        Assert.AreEqual(server2.ServerId, session.ServerId);

        // create tokens for server 1
        accessToken = await farm.CreateAccessToken(new AccessTokenCreateParams {
            Tags = ["#tag2"]
        });
        session = await accessToken.CreateSession(autoRedirect: true);
        Assert.AreEqual(server1.ServerId, session.ServerId);
    }

}