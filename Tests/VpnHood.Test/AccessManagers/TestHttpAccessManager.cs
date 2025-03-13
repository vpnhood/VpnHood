using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

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