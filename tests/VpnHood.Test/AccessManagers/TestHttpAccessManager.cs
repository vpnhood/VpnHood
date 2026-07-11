using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test.AccessManagers;

public class TestHttpAccessManager : HttpAccessManager
{
    public TestHttpAccessManagerServer HttpAccessManagerServer { get; }

    private TestHttpAccessManager(HttpAccessManagerOptions options,
        TestHttpAccessManagerServer httpAccessManagerServer) : base(options)
    {
        HttpAccessManagerServer = httpAccessManagerServer;
        Logger = VhLogger.Instance;
        LoggerEventId = GeneralEventId.AccessManager;
    }


    public static TestHttpAccessManager Create(IAccessManager baseAccessManager,
        bool autoDisposeBaseAccessManager = true)
    {
        var accessManagerServer = new TestHttpAccessManagerServer(baseAccessManager,
            autoDisposeBaseAccessManager: autoDisposeBaseAccessManager);
        accessManagerServer.Start();

        var accessManagerOptions = new HttpAccessManagerOptions(accessManagerServer.BaseUri, "Bearer");
        var httpAccessManager = new TestHttpAccessManager(accessManagerOptions, accessManagerServer);
        return httpAccessManager;
    }

    public override void Dispose()
    {
        HttpAccessManagerServer.Dispose();
        base.Dispose();
    }
}