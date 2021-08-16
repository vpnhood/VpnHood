
namespace VpnHood.Server.App
{
    class Program
    {
   
        static void Main(string[] args)
        {
            using ServerApp serverApp = new ServerApp();
            serverApp.Init(args);
        }
    }
}
