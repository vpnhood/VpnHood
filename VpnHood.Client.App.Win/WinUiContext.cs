using System.Security.Principal;
using System.Windows;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Win;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}

class UserIdProvider
{
    public string DeviceId => WindowsIdentity.GetCurrent().User?.Value ?? throw new Exception("Current user does not exist.");
}
