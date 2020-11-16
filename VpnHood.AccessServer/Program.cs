using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using VpnHood.Server;

namespace VpnHood.AccessServer
{
    public class Program
    {
        private static readonly AppUpdater _appUpdater = new AppUpdater();
        private static IHostApplicationLifetime _hostApplicationLifetime;

        public static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine($"AccessServer. Version: {typeof(Program).Assembly.GetName().Version}");

            if (_appUpdater.CheckNewerVersion())
            {
                _appUpdater.LaunchNewVersion();
                return;
            }
            _appUpdater.NewVersionFound += Updater_NewVersionFound;

            var host = CreateHostBuilder(args).Build();
            _hostApplicationLifetime = host.Services.GetService<IHostApplicationLifetime>();
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);
            host.Run();
        }

        private static void OnStopped()
        {
            _appUpdater.Dispose();
            if (_appUpdater.NewAppPath != null)
                _appUpdater.LaunchNewVersion();
        }

        private static void Updater_NewVersionFound(object sender, EventArgs e)
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
