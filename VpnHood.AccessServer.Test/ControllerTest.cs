using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace VpnHood.AccessServer.Test.Tests
{
    public class ControllerTest
    {
        protected TestInit TestInit1 { get; } = new();
        protected TestInit TestInit2 { get; } = new();

        [TestInitialize()]
        public virtual async Task Init()
        {
            await TestInit1.Init(true);
            await TestInit2.Init();
        }

        [TestCleanup()]
        public virtual void Cleanup()
        {
        }

    }
}
