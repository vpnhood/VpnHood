using System.Windows;
using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}
