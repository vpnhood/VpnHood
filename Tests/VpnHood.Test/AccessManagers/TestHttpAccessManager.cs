using VpnHood.Common.Logging;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Managers.Http;
using VpnHood.Tunneling;

namespace VpnHood.Test.AccessManagers;

public class TestHttpAccessManager : HttpAccessManager
{
    public TestEmbedIoAccessManager EmbedIoAccessManager { get; }

    private TestHttpAccessManager(HttpAccessManagerOptions options,
        TestEmbedIoAccessManager embedIoAccessManager) : base(options)
    {
        EmbedIoAccessManager = embedIoAccessManager;
        Logger = VhLogger.Instance;
        LoggerEventId = GeneralEventId.AccessManager;
    }


    public static TestHttpAccessManager Create(IAccessManager baseAccessManager,
        bool autoDisposeBaseAccessManager = true)
    {
        var embedIoAccessManager = new TestEmbedIoAccessManager(baseAccessManager,
            autoDisposeBaseAccessManager: autoDisposeBaseAccessManager);
        var accessManagerOptions = new HttpAccessManagerOptions(embedIoAccessManager.BaseUri, "Bearer");
        var httpAccessManager = new TestHttpAccessManager(accessManagerOptions, embedIoAccessManager);
        return httpAccessManager;
    }

    public override void Dispose()
    {
        EmbedIoAccessManager.Dispose();
        base.Dispose();
    }
}