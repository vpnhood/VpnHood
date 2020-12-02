using System;
using System.Linq;
using System.Threading;

namespace VpnHood.Client.App
{
    partial class Program
    {
        private static readonly Mutex _mutex = new Mutex(false, typeof(Program).FullName);
        private static WinConsole.ConsoleCtrlHandlerDelegate _consoleCtrlHandler;

        static void Main(string[] args)
        {
            var showConsole = args.Contains("/console");
            if (showConsole)
                WinConsole.ShowNewConsole();

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(0), false))
            {
                Console.WriteLine($"{nameof(App)} is already running!");
                return;
            }

            // run the app
            using var app = new App();
            app.Init(showConsole);
            if (showConsole)
            {
                _consoleCtrlHandler += sig => { app.Exit(); return true; };
                WinConsole.SetConsoleCtrlHandler(_consoleCtrlHandler, true);
            }
            app.Run();
        }
    }
}
