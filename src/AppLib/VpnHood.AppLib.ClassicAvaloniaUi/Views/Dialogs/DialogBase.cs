using Avalonia.Controls;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;

// A dialog the main view shows over the page: it answers once - through a button, or through Back,
// which the host turns into Close(false) - and the host takes it down. A dialog names the control
// the input lands on, as a page does, since a remote must find the ring on arrival.
public abstract class DialogBase : UserControl
{
    private readonly TaskCompletionSource<bool> _result = new();

    public Task<bool> Result => _result.Task;
    public event EventHandler? Closed;

    // false for the few that must not be dismissed by Back: a wait that the app is in the middle of
    public virtual bool CanDismiss => true;

    public abstract void FocusDefault();

    public void Close(bool answer = false)
    {
        if (!_result.TrySetResult(answer))
            return;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
