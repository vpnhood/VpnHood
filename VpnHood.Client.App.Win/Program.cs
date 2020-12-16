using System;
using System.Linq;
using System.Threading;

namespace VpnHood.Client.App
{
    partial class Program
    {

        static void Main(string[] args)
        {
            var noWindow = args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));

            // run the app
            using var app = new App();
            app.Start(openWindow: !noWindow, logToConsole: true);
        }
    }
}
