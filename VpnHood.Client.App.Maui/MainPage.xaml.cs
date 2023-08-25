using VpnHood.Client.App.UI;

namespace VpnHood.Client.App.Maui
{
    public partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
            MainWebView.Source = VpnHoodAppUi.Instance.Url.AbsoluteUri;
            MainWebView.Navigating += MainWebView_Navigating;
        }

        private void MainWebView_Navigating(object? sender, WebNavigatingEventArgs e)
        {
            e.Cancel = true;
            if (!new Uri(e.Url).Host.Equals(VpnHoodAppUi.Instance.Url.Host, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = Browser.Default.OpenAsync(e.Url, BrowserLaunchMode.SystemPreferred);
            }
        }
    }
}