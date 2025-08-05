using System.Windows;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

public class WinUiContext(Window window) : IUiContext
{
    public async Task<bool> IsActive()
    {
        try {
            return await window.Dispatcher.InvokeAsync(() => window.IsActive);
        }
        catch {
            return false; // If the window is destroyed, we assume it's not active
        }

    }

    // could not find a way to check if the window is destroyed in WPF,
    // so we assume it's not destroyed if it's active
    public async Task<bool> IsDestroyed()
    {
        try {
            return await window.Dispatcher.InvokeAsync(() => !window.IsActive);
        }
        catch {
            return false; // If the window is destroyed, we assume it's destroyed
        }
    }
}