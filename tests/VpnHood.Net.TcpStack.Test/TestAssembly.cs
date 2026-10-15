namespace VpnHood.Net.TcpStack.Test;

[TestClass]
public static class TestAssembly
{
    // MSTest's non-cooperative [Timeout] parks two thread-pool threads per running test; with the method-level
    // parallelism that is twice the pool's minimum, and under memory load the pool adds threads slowly or not at all,
    // so the whole host stalled for seconds and unrelated tests timed out. A floor above the parked threads keeps it
    // running; cooperative timeouts would park none, but a test that hangs would then hang the run.
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        ThreadPool.GetMinThreads(out _, out var completionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(64, Environment.ProcessorCount * 4), completionPortThreads);
    }
}
