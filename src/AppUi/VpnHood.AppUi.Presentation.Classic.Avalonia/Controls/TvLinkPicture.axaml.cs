using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// The picture over a browser's TV notices (MainView): IsBroken is the TV no longer answering.
public partial class TvLinkPicture : UserControl
{
    public static readonly StyledProperty<bool> IsBrokenProperty =
        AvaloniaProperty.Register<TvLinkPicture, bool>(nameof(IsBroken));

    public bool IsBroken {
        get => GetValue(IsBrokenProperty);
        set => SetValue(IsBrokenProperty, value);
    }

    public TvLinkPicture()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsBrokenProperty)
            RootGrid.Classes.Set("broken", IsBroken);
    }
}
