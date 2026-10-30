using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class PendingDialog : DialogBase
{
    // The store's sheet is on the device's own screen: a browser driving a TV says to go on there.
    public PendingDialog()
    {
        InitializeComponent();
        if (VhApp.IsTvRemote) {
            TitleText.Text = Strings.Current.ContinueOnTvTitle;
            TitleText.IsVisible = true;
            MessageText.Text = Strings.Current.ContinueOnTvDesc;
        }
    }

    // the store's answer is what closes it
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
    }
}
