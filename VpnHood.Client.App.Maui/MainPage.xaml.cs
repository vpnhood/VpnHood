using VpnHood.Client.App.UI;

namespace VpnHood.Client.App.Maui
{
    public partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
            MainWebView.Source = VpnHoodAppUi.Instance.Url.AbsoluteUri;
        }
    }
}