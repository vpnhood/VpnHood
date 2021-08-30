using System;

namespace VpnHood.Client.App
{
    internal class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // run the app
            using var app = new WinApp();
            app.Start(args);
        }
    }
}