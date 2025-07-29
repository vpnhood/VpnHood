using System.Windows;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public bool IsActive => window.IsActive;
    
    // could not find a way to check if the window is destroyed in WPF,
    // so we assume it's not destroyed if it's active
    public bool IsDestroyed => window.IsActive; 
}