using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.App.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var serverApp = new ServerApp();
        return await serverApp.Start(args, CancellationToken.None).Vhc();
    }
}