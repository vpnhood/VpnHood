using VpnHood.AppLib.App.Utils;

namespace VpnHood.App.Client;

// Client's settings: what its private appsettings can say (AppConfigs), which this project embeds
// once for every head. Client has no keys of its own; the type is its own so that the loader reads
// this project's files.
public class ClientAppConfigs : AppConfigs
{
    public static ClientAppConfigs Load() => AppConfigsLoader.Load<ClientAppConfigs>();
}
