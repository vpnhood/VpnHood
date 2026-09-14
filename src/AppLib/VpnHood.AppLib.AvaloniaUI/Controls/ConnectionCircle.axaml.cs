using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

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

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ConnectionCircle, double>(nameof(Progress));

    public static readonly StyledProperty<bool> IsProgressVisibleProperty =
        AvaloniaProperty.Register<ConnectionCircle, bool>(nameof(IsProgressVisible));

    public ConnectionCircle()
    {
        InitializeComponent();
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

    public double Progress {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsProgressVisible {
        get => GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (RootGrid == null)
            return;

        if (change.Property == PhaseProperty) {
            var phase = Phase;
            RootGrid.Classes.Set("live", phase != "none");
            RootGrid.Classes.Set("spinning", phase is "connecting" or "disconnecting");
            RootGrid.Classes.Set("connected", phase == "connected");
            RootGrid.Classes.Set("unstable", phase == "unstable");
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
        else if (change.Property == ProgressProperty) {
            Bar.Value = Progress;
            ProgressText.Text = $"{(int) Progress}%";
        }
        else if (change.Property == IsProgressVisibleProperty) {
            ProgressPanel.IsVisible = IsProgressVisible;
        }
    }
}
