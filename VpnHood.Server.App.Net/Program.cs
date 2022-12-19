using System;

namespace VpnHood.Server.App;

internal class Program
{
    private static void Main(string[] args)
    {
        using ServerApp serverApp = new();
        try
        {
            serverApp.Start(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}