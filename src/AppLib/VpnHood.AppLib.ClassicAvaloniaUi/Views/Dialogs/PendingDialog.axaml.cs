namespace VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;

public partial class PendingDialog : DialogBase
{
    public PendingDialog()
    {
        InitializeComponent();
    }

    // the store's answer is what closes it
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
