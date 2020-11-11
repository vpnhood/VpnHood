using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;

namespace VpnHood.AccessServer
{
    public class Program
    {
        public static VersionChecker versionChecker = new VersionChecker();

        public static void Main(string[] args)
        {
            if (versionChecker.CheckNewVersion())
                return;

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
