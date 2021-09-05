using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ProjectControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Crud()
        {
            var projectController = TestInit.CreateProjectController();
            var projectId = Guid.NewGuid();
            var project1A = await projectController.Create(projectId);
            Assert.AreEqual(projectId, project1A.ProjectId);

            var project1B = await projectController.Get(projectId);
            Assert.AreEqual(projectId, project1B.ProjectId);

            // make sure default group is created
            var accessPointGroupController = TestInit.CreateAccessPointGroupController();
            var accessPointGroups = await accessPointGroupController.List(projectId);
            Assert.IsTrue(accessPointGroups.Length > 0);
            Assert.IsTrue(accessPointGroups.Any(x => x.AccessPointGroup.IsDefault));

            // check a public and private token is created
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(projectId);
            Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
            Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));
        }
    }
}