using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Server;

internal class Program
{
    private static async Task Main(string[] args)
    {
        try {
            using var serverApp = new ServerApp();
            await serverApp.Start(args).Vhc();
        }
        catch (Exception ex) {
            throw new Exception(ex.Message);
        }
    }
}