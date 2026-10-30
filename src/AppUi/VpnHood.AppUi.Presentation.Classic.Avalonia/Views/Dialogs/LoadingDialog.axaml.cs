namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class LoadingDialog : DialogBase
{
    // "Please wait", or the host's words for what the wait is: a message, and a title over it for one
    // the person has a part in (MainView)
    public LoadingDialog(string? message = null, string? title = null)
    {
        InitializeComponent();
        if (message != null)
            MessageText.Text = message;
        if (title != null) {
            TitleText.Text = title;
            TitleText.IsVisible = true;
        }
    }

    // a wait is the app's, not the person's to end
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
