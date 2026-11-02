using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// UserReviewDialog's face: Rating is the rate it stands for, 1 sad to 3 happy.
public partial class RateFace : UserControl
{
    public static readonly StyledProperty<int> RatingProperty =
        AvaloniaProperty.Register<RateFace, int>(nameof(Rating));

    public static readonly StyledProperty<bool> IsPickedProperty =
        AvaloniaProperty.Register<RateFace, bool>(nameof(IsPicked));

    public int Rating {
        get => GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public bool IsPicked {
        get => GetValue(IsPickedProperty);
        set => SetValue(IsPickedProperty, value);
    }

    public RateFace()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RatingProperty) {
            Root.Classes.Set("sad", Rating == 1);
            Root.Classes.Set("neutral", Rating == 2);
            Root.Classes.Set("happy", Rating == 3);
        }
        else if (change.Property == IsPickedProperty) {
            Root.Classes.Set("picked", IsPicked);
        }
    }
}
