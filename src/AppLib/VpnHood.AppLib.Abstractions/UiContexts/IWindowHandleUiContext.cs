using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
namespace VpnHood.AppLib.Abstractions.UiContexts;

/// <summary>
/// Represents a desktop window as the UI context, with its native handle and its thread: the owner a Windows dialog
/// takes, as the Microsoft Store's own do (<c>IInitializeWithWindow</c>), and the thread it must be opened on.
/// </summary>
public interface IWindowHandleUiContext : IUiContext
{
    /// <summary>Gets the window's native handle.</summary>
    /// <returns>A task whose result is the handle.</returns>
    /// <exception cref="InvalidOperationException">The window has no native handle.</exception>
    Task<IntPtr> GetWindowHandle();

    /// <summary>Runs a function on the window's UI thread.</summary>
    /// <typeparam name="T">The function's result's type.</typeparam>
    /// <param name="action">The function.</param>
    /// <returns>A task whose result is the function's.</returns>
    Task<T> RunOnUiThread<T>(Func<T> action);
}
