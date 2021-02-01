using System;
using System.Linq;
using System.Threading;

namespace VpnHood.Client.App
{
    partial class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            // run the app
            using var app = new App();
            app.Start(args);
        }
    }
}
