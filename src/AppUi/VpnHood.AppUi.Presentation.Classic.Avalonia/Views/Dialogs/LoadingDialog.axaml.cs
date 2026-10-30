using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class LoadingDialog : DialogBase
{
    // "Please wait", or the host's words for what the wait is: a message, and a title over it for one
    // the person has a part in (MainView), with a picture of what it is about centred over them
    public LoadingDialog(string? message = null, string? title = null, Control? picture = null)
    {
        InitializeComponent();
        if (message != null)
            MessageText.Text = message;

        if (picture != null) {
            PictureHost.Content = picture;
            PictureHost.IsVisible = true;
        }

        if (title != null) {
            TitleText.Text = title;
            TitleText.IsVisible = true;

            // a titled wait is a notice of a few lines, which read badly centred: Material 3's dialog
            // with a hero picture - picture and title centred, the text from the start - with a small
            // spinner under it so it still says it is waiting
            Body.HorizontalAlignment = HorizontalAlignment.Stretch;
            MessageText.TextAlignment = TextAlignment.Start;
            Spinner.FontSize = 22;
            Body.Children.Move(Body.Children.IndexOf(Spinner), Body.Children.Count - 1);
        }
    }

    // a wait is the app's, not the person's to end
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
