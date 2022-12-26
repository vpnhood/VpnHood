using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AccessServer.Test;

public class BaseTest
{
    protected TestInit TestInit1 { get; private set; } = default!;


    [TestInitialize]
    public virtual async Task Init()
    {
        TestInit1 = await TestInit.Create(true, createServers: true);
    }

    [TestCleanup]
    public virtual void Cleanup()
    {
    }

}