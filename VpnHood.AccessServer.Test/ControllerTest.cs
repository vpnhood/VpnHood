using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AccessServer.Test;

public class ControllerTest
{
    protected TestInit TestInit1 { get; } = new();
    protected TestInit TestInit2 { get; } = new();

    [TestInitialize]
    public virtual async Task Init()
    {
        await TestInit1.Init(true);
        await TestInit2.Init();
    }

    [TestCleanup]
    public virtual void Cleanup()
    {
    }

}