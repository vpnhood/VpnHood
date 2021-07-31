﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Test
{
    public class ControllerTest
    {
        protected TestInit TestInit { get; } = new TestInit();

        [TestInitialize()]
        public virtual async Task Init()
        {
            await TestInit.Init();
        }

        [TestCleanup()]
        public virtual void Cleanup()
        {
        }

    }
}
