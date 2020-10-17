using VpnHood.Client.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VpnHood.Client;

namespace VpnHood.Test
{
    [TestClass]
    public class ClientApp_Test
    {
        class TestAppProvider : IAppProvider
        {
            public IDevice Device { get; } = TestHelper.CreateDevice();
        }


        [ClassInitialize]
        public static void Init(TestContext _)
        {
        }

        private VpnHoodApp CreateApp(string appPath = null)
        {
            //create app
            var appOptions = new AppOptions()
            {
                AppDataPath = appPath ?? Path.Combine(TestHelper.WorkingPath, "AppData_" + Guid.NewGuid())
            };
            return VpnHoodApp.Init(new TestAppProvider(), appOptions);
        }

        [TestMethod]
        public void Add_remove_clientProfiles()
        {
            using var app = CreateApp();

            // ************
            // *** TEST ***: AddAccessKey should add a clientProfile
            var clientProfiles = app.ClientProfileStore.ClientProfiles;
            var token1 = TestHelper.CreateDefaultTokenInfo(443).Token;
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            Assert.AreEqual(1, app.ClientProfileStore.ClientProfiles.Count(x => x.TokenId == token1.TokenId), "ClientProfile is not added");
            Assert.AreEqual(token1.TokenId, clientProfile1.TokenId, "invalid tokenId has been assigned to clientProfile");

            // ************
            // *** TEST ***: AddAccessKey with new accessKey should add another clientProfile
            var token2 = TestHelper.CreateDefaultTokenInfo(443).Token;
            app.ClientProfileStore.AddAccessKey(token2.ToAccessKey());
            Assert.AreEqual(1, app.ClientProfileStore.ClientProfiles.Count(x => x.TokenId == token2.TokenId), "ClientProfile is not added");

            // ************
            // *** TEST ***: AddAccessKey by same accessKey shoud just update token
            token1.Name = "Token 1000";
            app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            Assert.AreEqual(token1.Name, app.ClientProfileStore.GetToken(token1.TokenId).Name);

            // ************
            // *** TEST ***: SetClientProfile throw KeyNotFoundException exception if tokenId does not exist
            try
            {
                app.ClientProfileStore.SetClientProfile(new AppClientProfile
                {
                    Name = "Hi",
                    ClientProfileId = Guid.NewGuid(),
                    TokenId = Guid.NewGuid()
                });
                Assert.Fail("KeyNotFoundException exception was expected!");
            }
            catch (KeyNotFoundException) { }

            // ************
            // *** TEST ***: SetClientProfile should update the old node if ClientProfileId already exists
            app.ClientProfileStore.SetClientProfile(new AppClientProfile
            {
                Name = "Hi2",
                ClientProfileId = clientProfile1.ClientProfileId,
                TokenId = clientProfile1.TokenId
            });
            Assert.AreEqual("Hi2", app.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile1.ClientProfileId).Name);

            // ************
            // *** TEST ***: SetClientProfile should add new ClientProfile if ClientProfileId is new even with used tokenId
            clientProfiles = app.ClientProfileStore.ClientProfiles;
            var clientProfileId3 = Guid.NewGuid();
            app.ClientProfileStore.SetClientProfile(new AppClientProfile
            {
                Name = "Test-03",
                ClientProfileId = clientProfileId3,
                TokenId = clientProfile1.TokenId
            });
            Assert.AreEqual(clientProfiles.Length + 1, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not beed added!");

            // ************
            // *** TEST ***: RemoveClientProfile should not remove token when other clientProfile still use the token
            app.ClientProfileStore.RemoveClientProfile(clientProfileId3);
            Assert.AreEqual(clientProfiles.Length, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not been removed!");
            Assert.IsNotNull(app.ClientProfileStore.GetToken(clientProfile1.TokenId));

            // ************
            // *** TEST ***: RemoveClientProfile should remove token when no clientProfile usinng it
            clientProfiles = app.ClientProfileStore.ClientProfiles;
            app.ClientProfileStore.RemoveClientProfile(clientProfile1.ClientProfileId);
            Assert.AreEqual(clientProfiles.Length - 1, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not been removed!");
            try
            {
                app.ClientProfileStore.GetToken(clientProfile1.TokenId);
                Assert.Fail("KeyNotFoundException exception was expected!");
            }
            catch (KeyNotFoundException) { }

            // ************
            // *** TEST ***: ClientProfileItems
            Assert.AreEqual(app.ClientProfileStore.ClientProfiles.Length, app.ClientProfileStore.ClientProfileItems.Length, "ClientProfileItems has invalid length!");
        }

        [TestMethod]
        public void Token_secret_should_not_extracted()
        {
            using var app = CreateApp();

            // ************
            // *** TEST ***: AddClientProfile should not return then secret
            var token = TestHelper.CreateDefaultTokenInfo(443).Token;
            var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            // ************
            // *** TEST ***: GetToken should not return then secret
            var token2 = app.ClientProfileStore.GetToken(clientProfile.TokenId);
            Assert.IsNull(token2.Secret, "token should not have secret");

            // ************
            // *** TEST ***: ClientProfileItems should not return then secret
            var clientProfiles = app.ClientProfileStore.ClientProfileItems;
            Assert.IsTrue(clientProfiles.All(x => x.Token.Secret == null), "token should not have secret");
        }

        [TestMethod]
        public void Save_load_clientProfiles()
        {
            using var app = CreateApp();

            // ************
            // *** TEST ***: add 2 tokens and restore
            var token1 = TestHelper.CreateDefaultTokenInfo(443).Token;
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());

            var token2 = TestHelper.CreateDefaultTokenInfo(443).Token;
            var clientProfile2 = app.ClientProfileStore.AddAccessKey(token2.ToAccessKey());

            var clientProfiles = app.ClientProfileStore.ClientProfiles;
            app.Dispose();

            using var app2 = CreateApp(app.AppDataPath);
            Assert.AreEqual(clientProfiles.Length, app2.ClientProfileStore.ClientProfiles.Length, "ClientProfiles count are not same!");
            Assert.IsNotNull(app2.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile1.ClientProfileId));
            Assert.IsNotNull(app2.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile2.ClientProfileId));
            Assert.IsNotNull(app2.ClientProfileStore.GetToken(token1.TokenId));
            Assert.IsNotNull(app2.ClientProfileStore.GetToken(token2.TokenId));
        }
    }
}
