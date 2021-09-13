using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Auth.Models;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AuthTest
    {
        [TestMethod]
        public async Task GetAllParents()
        {
            await using VhContext vhContext = new();
        }
    }
}