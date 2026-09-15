using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using VpnHood.AppLib.AvaloniaUI.Animation;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

// Vuetify's v-ripple (directives/ripple, VRipple.css), value for value, for the buttons and the
// rows: the panel a control template puts around its content presenter, drawing over it. A press
// starts a circle of the text colour under the pointer, its radius the half-diagonal of the
// control, at 30% of its size and transparent: over 250ms it grows to full while it slides to the
// centre, and in 100ms it comes to its opacity (0.25 of the colour; a style doubles it on a light
// button, as Vuetify's overlay multiplier does). The release fades it out in 300ms, never before
// 250ms after it started. A finger's ripple waits 80ms, so a scroll that starts on a row does not
// flash one (a tap released sooner shows it on the release); Enter and Space ripple from the
// centre. The events are taken on the templated parent, with the pointer captured there on the
// press, so the release and a scroll's capture loss arrive whatever the pointer was over.
public class RippleEffect : Panel
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<RippleEffect, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> RippleOpacityProperty =
        AvaloniaProperty.Register<RippleEffect, double>(nameof(RippleOpacity), 0.25);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<RippleEffect>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<RippleEffect>();

    private static readonly TimeSpan TouchDelay = TimeSpan.FromMilliseconds(80);
    private readonly RippleLayer _layer;
    private Control? _host;
    private bool _keyHeld;

    public RippleEffect()
    {
        _layer = new RippleLayer(this) { IsHitTestVisible = false };
    }

    // The layer draws over the content, so it joins the children only once the template has
    // added the content: at the end of initialization, or on attachment when there was none.
    public override void EndInit()
    {
        base.EndInit();
        AddLayer();
    }

    private void AddLayer()
    {
        if (!Children.Contains(_layer))
            Children.Add(_layer);
    }

    public IBrush? Fill {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double RippleOpacity {
        get => GetValue(RippleOpacityProperty);
        set => SetValue(RippleOpacityProperty, value);
    }

    public CornerRadius CornerRadius {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Thickness BorderThickness {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AddLayer();
        _host = TemplatedParent as Control ?? this;
        _host.AddHandler(PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _host.AddHandler(PointerReleasedEvent, OnHostPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _host.AddHandler(PointerCaptureLostEvent, OnHostPointerCaptureLost, handledEventsToo: true);
        _host.AddHandler(KeyDownEvent, OnHostKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        _host.AddHandler(KeyUpEvent, OnHostKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host != null) {
            _host.RemoveHandler(PointerPressedEvent, OnHostPointerPressed);
            _host.RemoveHandler(PointerReleasedEvent, OnHostPointerReleased);
            _host.RemoveHandler(PointerCaptureLostEvent, OnHostPointerCaptureLost);
            _host.RemoveHandler(KeyDownEvent, OnHostKeyDown);
            _host.RemoveHandler(KeyUpEvent, OnHostKeyUp);
        }

        _host = null;
        _keyHeld = false;
        _layer.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_host is not { IsEffectivelyEnabled: true } host || _layer.IsHolding ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // A press on a button of the host's own - the menu in a server's header, the warning chip
        // in a settings card - is that button's: taking the capture here would keep the release
        // from it and it would never click. No ripple for the host either; the button has its own.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is { } pressed &&
            pressed != host && host.IsVisualAncestorOf(pressed))
            return;

        e.Pointer.Capture(host);
        var delay = e.Pointer.Type == PointerType.Touch ? TouchDelay : TimeSpan.Zero;
        _layer.Press(e.GetPosition(_layer), delay);
    }

    private void OnHostPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _layer.Release();
    }

    private void OnHostPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // the press moved the capture from the control under the pointer to the host: that loss
        // is not the host's
        if (ReferenceEquals(e.Source, _host))
            _layer.Cancel();
    }

    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (_keyHeld || e.Key is not (Key.Enter or Key.Space) || _layer.IsHolding ||
            _host is not { IsEffectivelyEnabled: true })
            return;

        _keyHeld = true;
        _layer.Press(null, TimeSpan.Zero);
    }

    private void OnHostKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_keyHeld)
            return;

        _keyHeld = false;
        _layer.Release();
    }

    // The ripples of one control, drawn on top of its content and ticked by the frame clock while
    // any is alive.
    private sealed class RippleLayer(RippleEffect owner) : Control
    {
        private static readonly Easing Decelerate = new SplineEasing(0, 0, 0.2);
        private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan FadeInTime = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan FadeOutTime = TimeSpan.FromMilliseconds(300);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Ripple> _ripples = [];
        private IDisposable? _frames;

        // a ripple is held from the press to the release or the capture loss
        public bool IsHolding => _ripples.Any(x => x.HideAt == null);

        // origin null: from the centre (a key)
        public void Press(Point? origin, TimeSpan delay)
        {
            _ripples.Add(new Ripple(origin, _clock.Elapsed + delay));
            _frames ??= FrameClock.OnFrame(Tick);
        }

        // A ripple still waiting shows now; the held one fades once it has had its 250ms.
        public void Release()
        {
            var now = _clock.Elapsed;
            foreach (var ripple in _ripples.Where(x => x.HideAt == null)) {
                if (ripple.ShowAt > now)
                    ripple.ShowAt = now;
                ripple.HideAt = Max(now, ripple.ShowAt + HoldTime);
            }
        }

        // A ripple still waiting is dropped (a scroll began on the control); a shown one fades.
        public void Cancel()
        {
            var now = _clock.Elapsed;
            _ripples.RemoveAll(x => x.HideAt == null && x.ShowAt > now);
            Release();
        }

        public void Clear()
        {
            _ripples.Clear();
            StopFrames();
        }

        private void Tick()
        {
            var now = _clock.Elapsed;
            _ripples.RemoveAll(x => x.HideAt != null && now >= x.HideAt + FadeOutTime);
            InvalidateVisual();
            if (_ripples.Count == 0)
                StopFrames();
        }

        private void StopFrames()
        {
            _frames?.Dispose();
            _frames = null;
        }

        public override void Render(DrawingContext context)
        {
            var border = owner.BorderThickness;
            var box = new Rect(Bounds.Size).Deflate(border);
            if (box.Width <= 0 || box.Height <= 0 || owner.Fill is not ISolidColorBrush { Color: var color })
                return;

            var now = _clock.Elapsed;
            var radius = Math.Sqrt(box.Width * box.Width + box.Height * box.Height) / 2;
            var centre = box.Center;
            using var clip = context.PushClip(InnerEdge(box, owner.CornerRadius, border));
            foreach (var ripple in _ripples) {
                var age = now - ripple.ShowAt;
                if (age < TimeSpan.Zero)
                    continue;

                var grow = Decelerate.Ease(Math.Min(1, age / GrowTime));
                var opacity = owner.RippleOpacity * Decelerate.Ease(Math.Min(1, age / FadeInTime));
                if (ripple.HideAt is { } hideAt && now >= hideAt)
                    opacity *= 1 - Decelerate.Ease(Math.Min(1, (now - hideAt) / FadeOutTime));

                var origin = ripple.Origin ?? centre;
                var at = origin + (centre - origin) * grow;
                var r = radius * (0.3 + 0.7 * grow);
                context.DrawEllipse(new ImmutableSolidColorBrush(color, opacity), null, at, r, r);
            }
        }

        // the clip: the control's rounded box inside its border, as CSS's border-radius: inherit
        // gives the ripple container
        private static RoundedRect InnerEdge(Rect box, CornerRadius corner, Thickness border)
        {
            return new RoundedRect(box,
                Radii(corner.TopLeft, Math.Max(border.Left, border.Top)),
                Radii(corner.TopRight, Math.Max(border.Right, border.Top)),
                Radii(corner.BottomRight, Math.Max(border.Right, border.Bottom)),
                Radii(corner.BottomLeft, Math.Max(border.Left, border.Bottom)));

            static Vector Radii(double radius, double inset)
            {
                var r = Math.Max(0, radius - inset);
                return new Vector(r, r);
            }
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b)
        {
            return a > b ? a : b;
        }

        private sealed class Ripple(Point? origin, TimeSpan showAt)
        {
            public Point? Origin => origin;
            public TimeSpan ShowAt { get; set; } = showAt;
            public TimeSpan? HideAt { get; set; }
        }
    }
}
