using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Reflection;
using VpnHood.Common;

namespace VpnHood.AccessServer
{
    public class Program
    {
        private static AppUpdater _appUpdater;
        private static IHostApplicationLifetime _hostApplicationLifetime;

        public static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine($"AccessServer. Version: {Assembly.GetEntryAssembly().GetName().Version}, Time: {DateTime.Now}");

            // check update
            _appUpdater = new AppUpdater(Directory.GetCurrentDirectory());
            _appUpdater.Updated += (sender, e) => _hostApplicationLifetime?.StopApplication();
            _appUpdater.Start();
            if (_appUpdater.IsUpdated)
            {
                _appUpdater.LaunchUpdated();
                return;
            }

            var host = CreateHostBuilder(args).Build();
            _hostApplicationLifetime = host.Services.GetService<IHostApplicationLifetime>();
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);
            host.Run();
        }

        private static void OnStopped()
        {
            // launch new version
            if (_appUpdater.IsUpdated)
                _appUpdater.LaunchUpdated();
            _appUpdater.Dispose();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
