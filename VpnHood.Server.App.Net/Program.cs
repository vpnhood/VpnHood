namespace VpnHood.Server.App;

internal class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            using var serverApp = new ServerApp();
            await serverApp.Start(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}