
namespace VpnHood.Server.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            using ServerApp serverApp = new();
            serverApp.Start(args);
        }
    }
}
