using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Reflection;
using VpnHood.Common;

namespace VpnHood.AccessServer
{
    public class Program
    {
        private static readonly AppUpdater _appUpdater = new AppUpdater(NullLogger.Instance);
        private static IHostApplicationLifetime _hostApplicationLifetime;

        public static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine($"AccessServer. Version: {Assembly.GetEntryAssembly().GetName().Version}, Time: {DateTime.Now}");

            _appUpdater.Published += Updater_Published;

            var host = CreateHostBuilder(args).Build();
            _hostApplicationLifetime = host.Services.GetService<IHostApplicationLifetime>();
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);
            host.Run();
        }

        private static void OnStopped()
        {
            _appUpdater.Dispose();
            if (_appUpdater.IsNewPublish)
                _appUpdater.LaunchNewPublish();
        }

        private static void Updater_Published(object sender, EventArgs e)
        {
            _hostApplicationLifetime?.StopApplication();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
