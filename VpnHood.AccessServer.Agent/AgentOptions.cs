using SessionOptions = VpnHood.Server.Access.Configurations;

namespace VpnHood.AccessServer.Agent;

public class AgentOptions
{
    public const string FarmTokenRepoHttpClientName = "FarmTokenRepoHttpClientName";
    public static readonly Version MinClientVersion = Version.Parse("2.3.289");
    public static readonly Version MinServerVersion = Version.Parse("3.0.411");
    private TimeSpan? _lostServerThreshold;

    public TimeSpan AdRewardDeviceTimeout { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan AdRewardTimeout { get; set; } = TimeSpan.FromMinutes(4);

    public TimeSpan ServerUpdateStatusInterval { get; set; } =
        new SessionOptions.ServerConfig().UpdateStatusIntervalValue;

    public TimeSpan LostServerThreshold {
        get => _lostServerThreshold ?? ServerUpdateStatusInterval * 3 + TimeSpan.FromSeconds(15);
        set => _lostServerThreshold = value;
    }

    public TimeSpan SessionSyncInterval { get; set; } = new SessionOptions.SessionOptions().SyncIntervalValue;
    public TimeSpan SessionTemporaryTimeout { get; set; } = new SessionOptions.SessionOptions().TimeoutValue;
    public long SyncCacheSize { get; set; } = new SessionOptions.SessionOptions().SyncCacheSizeValue;
    public TimeSpan SessionPermanentlyTimeout { get; set; } = TimeSpan.FromDays(2);
    public TimeSpan SaveCacheInterval { get; set; } = TimeSpan.FromMinutes(5);
    public string SystemAuthorizationCode { get; set; } = "";
    public bool AllowRedirect { get; set; } = true;
    public TimeSpan FarmTokenRepoUpdaterInterval { get; set; } = TimeSpan.FromMinutes(60);
}