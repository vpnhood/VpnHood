using System;
using System.Linq;
using System.Threading;

namespace VpnHood.Client.App
{
    partial class Program
    {
        private static readonly Mutex _mutex = new Mutex(false, typeof(Program).FullName);

        static void Main(string[] args)
        {
            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(0), false))
            {
                Console.WriteLine($"{nameof(App)} is already running!");
                return;
            }

            // run the app
            using var app = new App();
            app.Init(true);
            app.Run();
        }
    }
}
