using System;


namespace VpnHood.Client.App
{
    partial class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            // run the app
            using var app = new ClientApp();
            app.Start(args);
        }
    }
}
