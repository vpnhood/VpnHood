using VpnHood.Core.Common.Utils;

namespace VpnHood.Apps.Server;

internal class Program
{
    private static async Task Main(string[] args)
    {
        try {
            using var serverApp = new ServerApp();
            await serverApp.Start(args).VhConfigureAwait();
        }
        catch (Exception ex) {
            throw new Exception(ex.Message);
        }
    }
}