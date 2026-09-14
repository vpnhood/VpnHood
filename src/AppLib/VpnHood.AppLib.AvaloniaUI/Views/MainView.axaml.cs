using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The one page host, and the whole of the navigation: a stack of views, the home view at its
// bottom. Back - a remote's or a phone's key, which reaches Avalonia as the top level's
// BackRequested, or Escape and Backspace on a keyboard - pops; on the home view it is left to the
// host, so Android leaves the app as it should. Every view on the stack takes the input on arrival
// (FocusDefault): a remote has no pointer to put focus anywhere, and a keyboard should not need a
// Tab first.
public partial class MainView : UserControl
{
    private readonly MainViewModel _viewModel = new();
    private readonly Stack<IPage> _pages = new();
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();
        Navigate(new HomeView(_viewModel, this));
    }

    public void Navigate(IPage page)
    {
        _pages.Push(page);
        Show(page, back: false);
    }

    // False when there is nothing above the home view, so the caller can let the host have it.
    public bool GoBack()
    {
        if (_pages.Count <= 1)
            return false;

        var leaving = _pages.Pop();
        (leaving as IDisposable)?.Dispose();
        Show(_pages.Peek(), back: true);
        return true;
    }

    // The focus is asked for once the page is laid out: a control that is not yet measured cannot
    // take it, and a remote must find the ring on arrival. The direction picks the transition's
    // way, as the web UI's router does from the route depth.
    private void Show(IPage page, bool back)
    {
        Host.IsTransitionReversed = back;
        Host.Content = page;
        Dispatcher.UIThread.Post(page.FocusDefault, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
            _topLevel.BackRequested += OnBackRequested;
        Dispatcher.UIThread.Post(_pages.Peek().FocusDefault, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
            _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (GoBack())
            e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // a keyboard's stand-ins for the Back key, as on the web UI's TV mode
        if (e.Key is Key.Escape or Key.Back && GoBack()) {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}

// A page the host shows: it names the control the input lands on.
public interface IPage
{
    void FocusDefault();
}
