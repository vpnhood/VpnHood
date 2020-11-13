using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Threading;

namespace VpnHood.AccessServer
{
    public class Program
    {
        public static AppUpdater _appUpdater = new AppUpdater();
        private static IHostApplicationLifetime _hostApplicationLifetime;

        public static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine($"AccessServer. Version: {typeof(Program).Assembly.GetName().Version}");

            _appUpdater.NewVersionFound += Updater_NewVersionFound;
            if (_appUpdater.CheckNewerVersion(true))
            {
                Console.WriteLine($"Launching the new version!\n{_appUpdater.NewAppPath}");
                Process.Start(_appUpdater.NewAppPath);
                return;
            }

            var host = CreateHostBuilder(args).Build();
            _hostApplicationLifetime = host.Services.GetService<IHostApplicationLifetime>();
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);
            host.Run();
        }

        private static void OnStopped()
        {
            _appUpdater.Dispose();
            if (_appUpdater.NewAppPath!=null)
                Process.Start(_appUpdater.NewAppPath);
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
