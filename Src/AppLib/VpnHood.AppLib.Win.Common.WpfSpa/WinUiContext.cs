using System.Windows;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
}