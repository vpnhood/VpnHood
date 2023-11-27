using System;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Droid;

public class AppProvider : IAppProvider
{
    public required IDevice Device { get; init; } 
    public bool IsLogToConsoleSupported => false;
    public Uri? AdditionalUiUrl => null;
}