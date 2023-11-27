using System;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidAppProvider : IAppProvider
{
    public IDevice Device { get; }  = new AndroidDevice();
    public bool IsLogToConsoleSupported => false;
    public Uri? AdditionalUiUrl => null;
}