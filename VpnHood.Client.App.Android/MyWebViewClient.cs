using Android.Webkit;

namespace VpnHood.Client.App.Android
{
    class MyWebViewClient : WebViewClient
    {
        private readonly MainActivity _mainActivity;
        public MyWebViewClient(MainActivity mainActivity) { _mainActivity = mainActivity; }
        public override bool ShouldOverrideUrlLoading(WebView webView, IWebResourceRequest request)
        {
            return ShouldOverrideUrlLoading(webView, request);
        }

        public override void OnPageFinished(WebView view, string url)
        {
            base.OnPageFinished(view, url);
            _mainActivity.SetContentView(_mainActivity.WebView);
        }

        public override void OnPageCommitVisible(WebView view, string url) => base.OnPageCommitVisible(view, url);
    }
}