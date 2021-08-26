
using Microsoft.Extensions.Hosting;

namespace VpnHood.AccessServer
{
    class Program
    {
        public static void Main(string[] args)
        {
            using AccessServerApp accessServerApp = new();
            accessServerApp.Start(args);
        }

        // https://docs.microsoft.com/en-us/ef/core/cli/dbcontext-creation?tabs=dotnet-core-cli
        // for design time support
        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var accessServerApp = new AccessServerApp();
            accessServerApp.Start(new[] { "/designmode" });
            return accessServerApp.CreateHostBuilder(args);
        }
    }
}
