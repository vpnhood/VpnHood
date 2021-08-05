using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test
{
    public class ControllerTest
    {
        protected TestInit TestInit1 { get; } = new TestInit();
        protected TestInit TestInit2 { get; } = new TestInit();

        [TestInitialize()]
        public virtual async Task Init()
        {
            await TestInit1.Init(1);
            await TestInit2.Init();
        }

        [TestCleanup()]
        public virtual void Cleanup()
        {
        }

    }
}
