using System;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App;

internal class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            using var app = new WinApp();
            app.Start(args);
        }
        catch (AnotherInstanceIsRunning ex)
        {
            VhLogger.Instance.LogError(ex.Message);
        }
    }
}