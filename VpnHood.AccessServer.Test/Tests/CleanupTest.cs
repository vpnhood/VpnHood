using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CleanupTest : ControllerTest
{
    [TestMethod]
    public async Task ServerStatus_on_master()
    {
        var dateTime = DateTime.UtcNow.AddDays(-6000);

        var serverController = TestInit1.CreateServerController();
        var server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams());

        await using var vhContext = new VhContext();
        await vhContext.ServerStatuses.AddRangeAsync(
            new ServerStatusEx { ProjectId = TestInit1.ProjectId, ServerId = server.ServerId, CreatedTime = dateTime, IsLast = true },
            new ServerStatusEx { ProjectId = TestInit1.ProjectId, ServerId = server.ServerId, CreatedTime = dateTime, IsLast = false },
            new ServerStatusEx { ProjectId = TestInit1.ProjectId, ServerId = server.ServerId, CreatedTime = dateTime, IsLast = false },
            new ServerStatusEx { ProjectId = TestInit1.ProjectId, ServerId = server.ServerId, CreatedTime = dateTime, IsLast = false }
        );
        await vhContext.SaveChangesAsync();

        var cleanupManager = new CleanupManager(TestInit.CreateConsoleLogger<CleanupManager>());
        await cleanupManager.Cleanup();

        var res = await vhContext.ServerStatuses.Where(x => x.ServerId == server.ServerId).ToArrayAsync();
        Assert.AreEqual(1, res.Length);
        Assert.IsTrue(res[0].IsLast);
    }

}