using System.Windows;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}
