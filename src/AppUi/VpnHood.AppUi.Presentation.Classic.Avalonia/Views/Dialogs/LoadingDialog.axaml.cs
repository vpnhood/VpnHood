using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class LoadingDialog : DialogBase
{
    // A wait for what the device's own screen shows (isOnDevice) is answered on the TV when this UI
    // is the browser driving it, so it says that instead of its message.
    public LoadingDialog(string? message = null, bool isOnDevice = false)
    {
        InitializeComponent();
        if (isOnDevice && VhApp.IsTvRemote) {
            TitleText.Text = Strings.Current.ContinueOnTvTitle;
            TitleText.IsVisible = true;
            MessageText.Text = Strings.Current.ContinueOnTvDesc;
        }
        else if (message != null)
            MessageText.Text = message;
    }

    // a wait is the app's, not the person's to end
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
