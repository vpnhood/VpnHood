using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Animation;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.ViewModels;
using VpnHood.AppLib.AvaloniaUI.Views.Dialogs;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The one page host, and the whole of the navigation: a stack of views, the home view at its
// bottom. Back - a remote's or a phone's key, which reaches Avalonia as the top level's
// BackRequested, or Escape and Backspace on a keyboard - closes what is on top: a dialog, the
// drawer, then the page; on the home view it is left to the host, so Android leaves the app as
// it should. Every view on the stack takes the input on arrival (FocusDefault): a remote has no
// pointer to put focus anywhere, and a keyboard should not need a Tab first. The dialogs, the
// drawer and the snackbar the web UI's App.vue mounts once beside the router live here too, and
// a page reaches them through its host.
public partial class MainView : UserControl
{
    private static readonly TimeSpan SnackbarLife = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DrawerTime = TimeSpan.FromMilliseconds(200);
    private static readonly Easing DrawerEase = new SplineEasing(0.4, 0, 0.2);

    private readonly Stack<IPage> _pages = new();
    private readonly Stack<DialogBase> _dialogs = new();
    private readonly DispatcherTimer _snackbarTimer;
    private readonly TranslateTransform _drawerOffset = new();
    private CancellationTokenSource _drawerSlideCancel = new();
    private bool _isDrawerOpen;
    private DateTime _snackbarUntil;
    private TopLevel? _topLevel;
    private IInputElement? _focusBeforeOverlay;

    public MainViewModel ViewModel { get; } = new();

    public MainView()
    {
        InitializeComponent();
        // what a television gets and nothing else does - today the overscan inset of every page's
        // root (AppTheme). The device's answer never changes while the app runs, so it is a class,
        // not a binding.
        Classes.Set("tv", ViewModel.IsTv);
        _snackbarTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => TickSnackbar());
        ViewModel.PropertyChanged += (_, e) => {
            if (e.PropertyName is nameof(MainViewModel.IsReconnectRequired) or "")
                ReconnectBar.IsVisible = ViewModel.IsReconnectRequired;
        };
        Navigate(new HomeView(ViewModel, this));
        ViewModel.Host = this;

        DrawerContent.RenderTransform = _drawerOffset;

        // the consent a first run asks for, over everything until it is given (App.vue's
        // isShowPrivacyPolicyDialog)
        var features = AppData.Features;
        if (features.IsLicenseAgreementRequired && !AppData.UserSettings.IsLicenseAccepted)
            Navigate(new PrivacyPolicyView(this));
        // the account, once, for a build that has one (App.vue's onMounted); the pages that change
        // it read it again themselves
        if (features.IsAccountSupported)
            _ = LoadAccount();
    }

    private async Task LoadAccount()
    {
        try {
            await AppData.LoadAccount(false, CancellationToken.None);
            ViewModel.Refresh();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not load the account.");
        }
    }

    public IPage CurrentPage => _pages.Peek();

    public void Navigate(IPage page)
    {
        _pages.Push(page);
        Show(page, back: false);
    }

    // The web UI's router.replace: the page takes the current one's place, so Back skips it - the
    // drawer's rows, and the pages a dialog opens in place of the one beneath it. The home view is
    // the one page it never takes: replacing it would leave the arriving page at the bottom of the
    // stack with nothing to go back to, and its Back key dead. (The web UI has that hole - replace
    // over its first history entry, where router.go(-1) has nowhere to go - and Settings, opened
    // from the drawer, falls straight into it.)
    public void Replace(IPage page)
    {
        if (_pages.Count > 1)
            (_pages.Pop() as IDisposable)?.Dispose();
        _pages.Push(page);
        Show(page, back: false);
    }

    // back to the home view, popping everything above it
    public void GoHome()
    {
        while (_pages.Count > 1)
            (_pages.Pop() as IDisposable)?.Dispose();
        Show(_pages.Peek(), back: true);
    }

    // False when there is nothing above the home view, so the caller can let the host have it.
    // A page may hold the leave (ILeaveGuard): an edit that could not be saved says so instead.
    public bool GoBack()
    {
        if (_dialogs.Count > 0) {
            var top = _dialogs.Peek();
            if (top.CanDismiss)
                top.Close();
            return true;
        }

        if (_isDrawerOpen) {
            CloseDrawer();
            return true;
        }

        if (_pages.Count <= 1)
            return false;

        if (_pages.Peek() is ILeaveGuard guard) {
            _ = LeaveGuarded(guard);
            return true;
        }

        PopPage();
        return true;
    }

    private async Task LeaveGuarded(ILeaveGuard guard)
    {
        if (await guard.CanLeave())
            PopPage();
    }

    private void PopPage()
    {
        var leaving = _pages.Pop();
        (leaving as IDisposable)?.Dispose();
        Show(_pages.Peek(), back: true);
    }

    // The focus is asked for once the page is laid out: a control that is not yet measured cannot
    // take it, and a remote must find the ring on arrival. The direction picks the transition's
    // way, as the web UI's router does from the route depth.
    private void Show(IPage page, bool back)
    {
        // The page on its way out takes no more input. The swap is not instant - the transition
        // keeps the leaving page in the tree while it fades - so the second click of a double-click
        // still lands on the row that was just pressed and opens its page a second time; the stack
        // then holds two of them and Back looks dead, because it comes back to the same page. (The
        // web UI is spared this by its router, which drops a push of the route it is already on.)
        if (Host.Content is Control leaving)
            leaving.IsHitTestVisible = false;
        if (page is Control arriving)
            arriving.IsHitTestVisible = true;

        Host.IsTransitionReversed = back;
        Host.Content = page;
        Dispatcher.UIThread.Post(page.FocusDefault, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null) {
            _topLevel.BackRequested += OnBackRequested;
            _topLevel.AddHandler(KeyDownEvent, OnTopLevelKeyDown);
        }
        Dispatcher.UIThread.Post(_pages.Peek().FocusDefault, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null) {
            _topLevel.BackRequested -= OnBackRequested;
            _topLevel.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        }
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (GoBack())
            e.Handled = true;
    }

    // A keyboard's stand-ins for the Back key, as on the web UI's TV mode - not Backspace in a
    // field, where it deletes. Heard on the top level rather than here: a key with nothing focused
    // (a dialog just closed over a control that is gone) is delivered to the window, above this
    // view, and Back must work then too.
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        var isTyping = _topLevel?.FocusManager.GetFocusedElement() is TextBox;
        if ((e.Key == Key.Escape || (e.Key == Key.Back && !isTyping)) && GoBack())
            e.Handled = true;
    }

    // ---- dialogs ----

    // Shows a dialog over the page and answers when it closes. The page beneath is disabled
    // meanwhile: a disabled control cannot take the focus, which is what keeps a remote's walk
    // inside the dialog. The focus goes back where it was when the last dialog is gone.
    public Task<bool> ShowDialog(DialogBase dialog)
    {
        if (_dialogs.Count == 0)
            _focusBeforeOverlay = _topLevel?.FocusManager.GetFocusedElement();

        _dialogs.Push(dialog);
        dialog.Closed += OnDialogClosed;
        ShowTopDialog();
        return dialog.Result;
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not DialogBase dialog)
            return;
        dialog.Closed -= OnDialogClosed;

        // closed out of order (a wait ended under a question): take it out wherever it is
        var remaining = _dialogs.Where(x => x != dialog).Reverse().ToArray();
        _dialogs.Clear();
        foreach (var item in remaining)
            _dialogs.Push(item);
        ShowTopDialog();
    }

    private void ShowTopDialog()
    {
        if (_dialogs.Count == 0) {
            DialogLayer.IsVisible = false;
            DialogContent.Content = null;
            Host.IsEnabled = !DrawerLayer.IsVisible;
            RestoreFocus();
            return;
        }

        var top = _dialogs.Peek();
        Host.IsEnabled = false;
        DrawerLayer.IsEnabled = false;
        DialogLayer.IsVisible = true;
        DialogContent.Content = top;
        Dispatcher.UIThread.Post(top.FocusDefault, DispatcherPriority.Loaded);
    }

    private void RestoreFocus()
    {
        DrawerLayer.IsEnabled = true;
        var target = _focusBeforeOverlay;
        _focusBeforeOverlay = null;
        if (target is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control)
            Dispatcher.UIThread.Post(control.LandFocus, DispatcherPriority.Loaded);
    }

    public Task<bool> Confirm(string title, string message)
    {
        return ShowDialog(new ConfirmDialog(title, message));
    }

    public Task ShowError(string message, ErrorActions? actions = null)
    {
        return ShowDialog(new ErrorDialog(this, message, actions));
    }

    // The web UI's processError: the sentence for the failure, with its buttons; a private DNS
    // error opens its page instead, and a silenced one shows nothing.
    public async Task ProcessError(Exception exception)
    {
        VhLogger.Instance.LogError(exception, "The UI caught an error.");
        var message = ErrorMessages.For(exception, AppData.ErrorContext);
        await ShowErrorMessage(message);
    }

    public async Task ShowErrorMessage(ErrorMessage message)
    {
        if (message.IsIgnored)
            return;

        if (message.Actions?.IsPrivateDnsError == true && AppData.IsPremiumFeature(AppFeature.CustomDns)) {
            Navigate(FeaturePages.PrivateDnsError(this));
            await AppData.Api.App.ClearLastError(CancellationToken.None);
            return;
        }

        await ShowError(message.Text, message.Actions);
    }

    // a wait the app is in: shown until the scope is disposed
    public IDisposable Loading(string? message = null)
    {
        var dialog = new LoadingDialog(message);
        _ = ShowDialog(dialog);
        return new LoadingScope(dialog);
    }

    private sealed class LoadingScope(LoadingDialog dialog) : IDisposable
    {
        public void Dispose() => dialog.Close();
    }

    // ---- the snackbar ----

    public void ShowSnackbar(string message, SnackbarKind kind = SnackbarKind.Highlight, bool hasTimer = true, bool? hasClose = null)
    {
        Snackbar.Classes.Set("active", kind == SnackbarKind.Active);
        Snackbar.Classes.Set("warning", kind == SnackbarKind.Warning);
        Snackbar.Classes.Set("suppress", kind == SnackbarKind.Suppress);
        SnackbarIcon.Text = kind switch {
            SnackbarKind.Active => Mdi.CheckCircle,
            SnackbarKind.Warning => Mdi.AlertCircle,
            _ => Mdi.Information
        };
        SnackbarText.Text = message;
        SnackbarClose.IsVisible = hasClose ?? !hasTimer;
        SnackbarBar.IsVisible = hasTimer;
        SnackbarBar.Value = 1;
        Snackbar.IsVisible = true;
        if (hasTimer) {
            _snackbarUntil = DateTime.UtcNow + SnackbarLife;
            _snackbarTimer.Start();
        }
        else {
            _snackbarTimer.Stop();
        }
    }

    public void HideSnackbar()
    {
        _snackbarTimer.Stop();
        Snackbar.IsVisible = false;
        UpdateNotice.IsVisible = false;
    }

    public bool IsSnackbarShown => Snackbar.IsVisible;
    public bool IsUpdateNoticeShown => UpdateNotice.IsVisible;

    // UpdateSnackbar.vue: a newer version, with the way to it - the store, else the direct link -
    // and Later, which postpones the notice
    public void ShowUpdateNotice(AppUpdaterStatus status)
    {
        var publish = status.PublishInfo;
        if (publish == null)
            return;
        var isDeprecated = status.VersionStatus == VersionStatus.Deprecated;
        UpdateNotice.Classes.Set("update-warning", isDeprecated);
        UpdateNotice.Classes.Set("update-alert", !isDeprecated);
        UpdateNoticeText.Text = isDeprecated ? Strings.Current.VersionIsDeprecated : Strings.Current.VersionIsOld;
        UpdateStoreButton.IsVisible = publish.GooglePlayUrl != null;
        UpdateDirectButton.IsVisible = publish.GooglePlayUrl == null;
        UpdateNoStoreButton.IsVisible = publish.GooglePlayUrl != null;
        UpdateAlternative.IsVisible = false;
        UpdateCurrentText.Text = $"{Strings.Current.CurrentVersion} {AppData.Features.Version.ToString(3)}";
        UpdateNewText.Text = $"{Strings.Current.NewVersion} {publish.Version}";
        UpdateNotice.IsVisible = true;
    }

    private async void OnUpdateStoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppData.State.UpdaterStatus?.PublishInfo?.GooglePlayUrl is { } url)
                await OpenLink(url, Strings.Current.UpdateFromGooglePlay);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnUpdateDirectClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppData.State.UpdaterStatus?.PublishInfo?.InstallationPageUrl is { } url)
                await OpenLink(url, Strings.Current.UpdateFromDirectLink);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnUpdateNoStoreClick(object? sender, RoutedEventArgs e)
    {
        UpdateNoStoreButton.IsVisible = false;
        UpdateAlternative.IsVisible = true;
    }

    private void OnUpdateLaterClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.PostponeUpdate();
        UpdateNotice.IsVisible = false;
    }

    private void TickSnackbar()
    {
        var left = _snackbarUntil - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) {
            HideSnackbar();
            return;
        }
        SnackbarBar.Value = left / SnackbarLife;
    }

    private void OnSnackbarCloseClick(object? sender, RoutedEventArgs e)
    {
        // the 'suppressed to' notice stays away until a new connection is made
        ViewModel.IgnoreSuppressNotice();
        HideSnackbar();
    }

    // ---- the reconnect bar ----

    private async void OnReconnectClick(object? sender, RoutedEventArgs e)
    {
        try {
            await ViewModel.Reconnect();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnReconnectDismissClick(object? sender, RoutedEventArgs e)
    {
        try {
            await ViewModel.ClearReconnectRequired();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // ---- the drawer ----

    public void OpenDrawer()
    {
        if (_isDrawerOpen)
            return;

        _isDrawerOpen = true;
        _focusBeforeOverlay = _topLevel?.FocusManager.GetFocusedElement();
        var drawer = new DrawerView(this);
        DrawerContent.Content = drawer;
        DrawerLayer.IsVisible = true;
        Host.IsEnabled = false;
        Dispatcher.UIThread.Post(drawer.FocusDefault, DispatcherPriority.Loaded);
        _ = SlideDrawer(open: true);
    }

    // The page behind, the focus and the input are handed back at once; only the picture waits for
    // the slide, so a Back key never feels held by an animation.
    public void CloseDrawer()
    {
        if (!_isDrawerOpen)
            return;

        _isDrawerOpen = false;
        if (_dialogs.Count == 0) {
            Host.IsEnabled = true;
            RestoreFocus();
        }

        _ = SlideDrawer(open: false);
    }

    // Vuetify's temporary navigation drawer, value for value: the panel slides in from the start
    // edge and the scrim comes up with it, over 200ms on the standard curve. The offset is the
    // drawer's own measured width rather than the 300 of its style, and it is taken before the
    // first frame is drawn, so the panel is never seen in place. A slide that is still running when
    // the other one starts is cancelled where it is, and the one taking over carries on from there.
    private async Task SlideDrawer(bool open)
    {
        await _drawerSlideCancel.CancelAsync();
        _drawerSlideCancel.Dispose();
        _drawerSlideCancel = new CancellationTokenSource();
        var cancellationToken = _drawerSlideCancel.Token;

        DrawerContent.Measure(Size.Infinity);
        var hidden = -DrawerContent.DesiredSize.Width;
        await FrameClock.Tween(DrawerTime, DrawerEase, progress => {
            var shown = open ? progress : 1 - progress;
            _drawerOffset.X = hidden * (1 - shown);
            DrawerScrim.Opacity = shown;
        }, cancellationToken);

        // the layer holds the drawer while it leaves, and goes once it has
        if (!open && !cancellationToken.IsCancellationRequested) {
            DrawerLayer.IsVisible = false;
            DrawerContent.Content = null;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseDrawer();
    }

    // ---- outbound links ----

    // The web UI's onExternalLinkClick: a TV is not guaranteed a browser, so there the link
    // becomes a code to scan; anywhere else the device's browser opens it.
    public async Task OpenLink(Uri url, string title)
    {
        if (AppData.IsTvUi) {
            await ShowDialog(new OpenOnPhoneDialog(url, title));
            return;
        }

        var launcher = _topLevel?.Launcher;
        if (launcher == null || !await launcher.LaunchUriAsync(url))
            await ShowDialog(new OpenOnPhoneDialog(url, title));
    }

    // whether a link can get anywhere from this device (VpnHoodApp.isExternalLinkUsable)
    public static bool IsExternalLinkUsable => AppData.Intents.IsWebBrowserSupported || AppData.IsTvUi;
}
