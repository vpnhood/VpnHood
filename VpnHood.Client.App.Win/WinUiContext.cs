using System.Windows;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Win;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}