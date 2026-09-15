namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class LoadingDialog : DialogBase
{
    public LoadingDialog(string? message = null)
    {
        InitializeComponent();
        if (message != null)
            MessageText.Text = message;
    }

    // a wait is the app's, not the person's to end
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
