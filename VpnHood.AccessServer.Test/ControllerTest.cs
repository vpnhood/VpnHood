using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test
{
    public class ControllerTest
    {
        private TransactionScope _trans;
        protected VhContext VhContext { get; private set; }

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            VhContext = new();
            TestInit.Init().Wait();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            VhContext.Dispose();
            _trans.Dispose();
        }

    }
}
