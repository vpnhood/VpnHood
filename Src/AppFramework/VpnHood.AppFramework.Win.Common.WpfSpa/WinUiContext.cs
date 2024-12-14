using System.Windows;
using VpnHood.Client.Device;

namespace VpnHood.AppFramework.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}
