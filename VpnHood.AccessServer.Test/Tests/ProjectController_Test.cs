using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ProjectController_Test : ControllerTest
    {
        [TestMethod]
        public async Task CRUD()
        {
            var projectController = TestInit.CreateProjectController();
            var projectId = Guid.NewGuid();
            var project1A = await projectController.Create(projectId: projectId);
            Assert.AreEqual(projectId, project1A.ProjectId);

            var project1B = await projectController.Get(projectId);
            Assert.AreEqual(projectId, project1B.ProjectId);

            // make sure default groupid is created
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var accessTokenGroups = await accessTokenGroupController.List(projectId);
            Assert.IsTrue(accessTokenGroups.Length > 0);
            Assert.IsTrue(accessTokenGroups.Any(x => x.AccessTokenGroup.IsDefault));

            // check a public and private token is created
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(projectId);
            Assert.IsTrue(accessTokens.Any(x => x.AccessToken.IsPublic));
            Assert.IsTrue(accessTokens.Any(x => !x.AccessToken.IsPublic));
        }
    }
}
