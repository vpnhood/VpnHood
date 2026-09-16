using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VpnHood.AppLib.ClassicAvaloniaUi.Animation;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Controls;

// The connection circle's properties, pushed into its named parts. Phase is the web UI's class on
// the indicator: none, connecting, waiting, connected, unstable, disconnecting.
public partial class ConnectionCircle : UserControl
{
    public static readonly StyledProperty<string> PhaseProperty =
        AvaloniaProperty.Register<ConnectionCircle, string>(nameof(Phase), "none");

    public static readonly StyledProperty<string> StateTextProperty =
        AvaloniaProperty.Register<ConnectionCircle, string>(nameof(StateText), "");

    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<ConnectionCircle, string>(nameof(Glyph), "");

    public static readonly StyledProperty<string> UsageTextProperty =
        AvaloniaProperty.Register<ConnectionCircle, string>(nameof(UsageText), "");

    public static readonly StyledProperty<string> ExpireTextProperty =
        AvaloniaProperty.Register<ConnectionCircle, string>(nameof(ExpireText), "");

    public static readonly StyledProperty<bool> IsExpireWarningProperty =
        AvaloniaProperty.Register<ConnectionCircle, bool>(nameof(IsExpireWarning));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ConnectionCircle, double>(nameof(Progress));

    public static readonly StyledProperty<bool> IsProgressVisibleProperty =
        AvaloniaProperty.Register<ConnectionCircle, bool>(nameof(IsProgressVisible));

    // The dots' turn (#rotateCircle). The web UI's turn never ends: it is a 3s linear rotate whose
    // play state goes from running to paused, so a connect freezes the ring at the angle it had
    // reached and the dots slide to the centre from there. A style animation has no pause - when
    // the class goes the angle reverts to its base value in one frame, and the dots jump back to
    // the top of the circle before the connect animation plays, which is the cut this avoids. So
    // the angle is turned here, a frame at a time, and simply left where it is when the turn
    // stops; the next one picks it up from there.
    private static readonly TimeSpan TurnTime = TimeSpan.FromSeconds(3);
    private readonly RotateTransform _turn = new();
    private IDisposable? _turnFrames;
    private long _turnFrameAt;
    private bool _isAttached;
    private bool _sawDisconnected;

    public ConnectionCircle()
    {
        InitializeComponent();
        Rotor.RenderTransform = _turn;
    }

    public string Phase {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public string StateText {
        get => GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
    }

    public string Glyph {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string UsageText {
        get => GetValue(UsageTextProperty);
        set => SetValue(UsageTextProperty, value);
    }

    public string ExpireText {
        get => GetValue(ExpireTextProperty);
        set => SetValue(ExpireTextProperty, value);
    }

    public bool IsExpireWarning {
        get => GetValue(IsExpireWarningProperty);
        set => SetValue(IsExpireWarningProperty, value);
    }

    public double Progress {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsProgressVisible {
        get => GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    // The attachment is the circle's mount. The turn runs only while it is on screen: the frame
    // clock holds the tick, and a ticking circle that has left the tree would keep it alive and the
    // timer running. The connect animation is forgotten on the way out, so it is never in hand when
    // the circle comes back - a style's animations run again every time the style is applied, and
    // clearing the class on arrival would be one application too late.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateConnected();
        UpdateTurn();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _sawDisconnected = false;
        RootGrid.Classes.Remove("animate");
        UpdateTurn();
        base.OnDetachedFromVisualTree(e);
    }

    // The connect animation belongs to a connect this page watched happen: the web UI's
    // showConnectedAnimation, which starts null on every mount, so a page that opens on a live
    // session shows the connected picture and nothing moves (its .animation-false). The circle
    // outlives the page it sits on, so leaving home forgets what it saw and a return finds a
    // session it did not watch begin - the arrival the user reads as "just connected" is exactly
    // what must not play. The classes are the split in the styles: .connected is the picture,
    // .animate brings the animations, and it goes on first so one application carries both.
    private void UpdateConnected()
    {
        var isConnected = Phase == "connected";
        if (!isConnected)
            _sawDisconnected = _isAttached;

        RootGrid.Classes.Set("animate", isConnected && _sawDisconnected);
        RootGrid.Classes.Set("connected", isConnected);
    }

    private void UpdateTurn()
    {
        var isTurning = _isAttached && Phase is "connecting" or "disconnecting";
        if (isTurning == (_turnFrames != null))
            return;

        if (!isTurning) {
            _turnFrames?.Dispose();
            _turnFrames = null;
            return;
        }

        _turnFrameAt = Stopwatch.GetTimestamp();
        _turnFrames = FrameClock.OnFrame(OnTurnFrame);
    }

    private void OnTurnFrame()
    {
        var now = Stopwatch.GetTimestamp();
        _turn.Angle = (_turn.Angle + Stopwatch.GetElapsedTime(_turnFrameAt, now) / TurnTime * 360) % 360;
        _turnFrameAt = now;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (RootGrid == null)
            return;

        if (change.Property == PhaseProperty) {
            var phase = Phase;
            RootGrid.Classes.Set("live", phase != "none");
            RootGrid.Classes.Set("unstable", phase == "unstable");
            UpdateConnected();
            UpdateTurn();
        }
        else if (change.Property == StateTextProperty) {
            StateTextBlock.Text = StateText;
        }
        else if (change.Property == GlyphProperty) {
            GlyphBlock.Text = Glyph;
            GlyphBlock.IsVisible = Glyph.Length > 0;
        }
        else if (change.Property == UsageTextProperty) {
            UsageBlock.Text = UsageText;
            UsageBlock.IsVisible = UsageText.Length > 0;
        }
        else if (change.Property == ExpireTextProperty) {
            ExpireBlock.Text = ExpireText;
            ExpireBlock.IsVisible = ExpireText.Length > 0;
        }
        else if (change.Property == IsExpireWarningProperty) {
            ExpireBlock.Foreground = this.FindResource(IsExpireWarning ? "ExpireDateWarningBrush" : "ExpireDateAlertBrush") as IBrush;
        }
        else if (change.Property == ProgressProperty) {
            Bar.Value = Progress;
            ProgressText.Text = $"{(int) Progress}%";
        }
        else if (change.Property == IsProgressVisibleProperty) {
            ProgressPanel.IsVisible = IsProgressVisible;
        }
    }
}
