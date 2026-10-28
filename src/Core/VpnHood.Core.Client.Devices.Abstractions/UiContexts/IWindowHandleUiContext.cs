namespace VpnHood.Core.Client.Devices.Abstractions.UiContexts;

// A desktop window as the UI context, with its native handle and its thread: the owner a Windows dialog a desktop
// app opens takes, as the Microsoft Store's own do (IInitializeWithWindow), and the thread it must be opened on.
public interface IWindowHandleUiContext : IUiContext
{
    Task<IntPtr> GetWindowHandle();
    Task<T> RunOnUiThread<T>(Func<T> action);
}
