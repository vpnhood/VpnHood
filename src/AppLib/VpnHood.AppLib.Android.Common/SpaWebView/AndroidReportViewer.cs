using Android.Content;
using Android.Views;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using Uri = System.Uri;

namespace VpnHood.AppLib.Droid.Common.SpaWebView;

// In-app viewer for the on-device report/log — the loopback URL the SPA opens via window.open (e.g. the
// "open log" button). Mirrors the iOS report viewer: shows the report in a full-screen dialog WebView with a
// find-on-page bar (Search) and an export action (Share), instead of kicking the user out to an external
// browser that can't reach the loopback server. Uses only framework APIs — no extra dependencies.
internal sealed class AndroidReportViewer
{
    private readonly Activity _activity;
    private readonly Uri _reportUri;
    private readonly WebView _webView;
    private readonly Dialog _dialog;
    private readonly TextView _matchCountView;
    private readonly LinearLayout _findBar;

    private AndroidReportViewer(Activity activity, Uri reportUri)
    {
        _activity = activity;
        _reportUri = reportUri;

        _webView = new WebView(activity);
        _webView.Settings.JavaScriptEnabled = false; // the report is plain text; no JS needed
        _webView.SetFindListener(new FindListener(OnFindResult));

        _matchCountView = new TextView(activity);
        _findBar = BuildFindBar();

        var root = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        root.AddView(BuildTopBar(), MatchWrap());
        root.AddView(_findBar, MatchWrap());
        root.AddView(_webView,
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        _dialog = new Dialog(activity);
        _dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
        _dialog.SetContentView(root);
        _dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
    }

    public static void Show(Activity activity, Uri reportUri)
    {
        var viewer = new AndroidReportViewer(activity, reportUri);
        viewer._webView.LoadUrl(reportUri.ToString());
        viewer._dialog.Show();
    }

    private LinearLayout BuildTopBar()
    {
        var bar = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));

        var closeBtn = IconButton(Android.Resource.Drawable.IcMenuCloseClearCancel);
        closeBtn.Click += (_, _) => _dialog.Dismiss();

        var title = new TextView(_activity) { Text = ReportFileName() };
        title.SetPadding(Dp(8), 0, Dp(8), 0);

        var searchBtn = IconButton(Android.Resource.Drawable.IcMenuSearch);
        searchBtn.Click += (_, _) => ToggleFindBar();

        // Re-fetch the report from its (loopback) source and reload, so the user can pull the latest log
        // without closing and reopening the viewer. The WebView loads straight from the URL, so a fresh
        // LoadUrl re-requests it — no separate download step is needed (unlike the iOS viewer).
        var refreshBtn = IconButton(Android.Resource.Drawable.IcPopupSync);
        refreshBtn.Click += (_, _) => _webView.LoadUrl(_reportUri.ToString());

        var shareBtn = IconButton(Android.Resource.Drawable.IcMenuShare);
        shareBtn.Click += (_, _) => _ = ShareAsync();

        bar.AddView(closeBtn);
        bar.AddView(title);
        bar.AddView(new Space(_activity),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        bar.AddView(searchBtn);
        bar.AddView(refreshBtn);
        bar.AddView(shareBtn);
        return bar;
    }

    private LinearLayout BuildFindBar()
    {
        var bar = new LinearLayout(_activity) {
            Orientation = Orientation.Horizontal,
            Visibility = ViewStates.Gone
        };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));

        var input = new EditText(_activity) { Hint = "Find in page" };
        input.SetSingleLine(true);
        input.TextChanged += (_, _) => {
            var query = input.Text ?? "";
            if (string.IsNullOrEmpty(query))
                _webView.ClearMatches();
            else
                _webView.FindAllAsync(query);
        };

        var prev = new Button(_activity) { Text = "‹" };
        prev.Click += (_, _) => _webView.FindNext(false);

        var next = new Button(_activity) { Text = "›" };
        next.Click += (_, _) => _webView.FindNext(true);

        bar.AddView(input, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        bar.AddView(_matchCountView);
        bar.AddView(prev);
        bar.AddView(next);
        return bar;
    }

    private void ToggleFindBar()
    {
        if (_findBar.Visibility == ViewStates.Visible) {
            _findBar.Visibility = ViewStates.Gone;
            _webView.ClearMatches();
        }
        else {
            _findBar.Visibility = ViewStates.Visible;
        }
    }

    private void OnFindResult(int activeMatchOrdinal, int numberOfMatches)
    {
        _matchCountView.Text = numberOfMatches > 0 ? $"{activeMatchOrdinal + 1}/{numberOfMatches}" : "0/0";
    }

    // Browsers that can open the live log page. Mirrors the iOS viewer's browser list; used to give only these
    // targets the loopback URL (see ShareAsync).
    // ReSharper disable StringLiteralTypo
    private static readonly string[] BrowserPackages = [
        "com.android.chrome",
        "org.mozilla.firefox",
        "com.microsoft.emmx",           // Edge
        "com.opera.browser",
        "com.opera.mini.native",
        "com.brave.browser",
        "com.duckduckgo.mobile.android",
        "com.sec.android.app.sbrowser"  // Samsung Internet
    ];
    // ReSharper restore StringLiteralTypo

    // Export the report as an actual file (content:// via ReportFileProvider), never as raw text: handing over
    // the whole log as Intent.ExtraText makes text-first apps (e.g. Telegram) paste the entire huge log and can
    // exceed the intent transaction limit. File-consuming apps (Telegram/Mail/Files/Drive) therefore get only
    // the attached file — the loopback URL is useless off-device. Browsers can't render the file but can open
    // the live log page, so via ExtraReplacementExtras those targets alone receive the loopback URL.
    private async Task ShareAsync()
    {
        try {
            // ReSharper disable once ShortLivedHttpClient
            // we rarely use it and don't want to keep a static instance around; 
            using var httpClient = new HttpClient();
            var content = await httpClient.GetByteArrayAsync(_reportUri);
            var contentUri = ReportFileProvider.SaveAndGetUri(_activity, ReportFileName(), content);

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraSubject, ReportFileName());
            intent.PutExtra(Intent.ExtraStream, contentUri);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            var chooser = Intent.CreateChooser(intent, "Share log")!;
            var replacements = new Bundle();
            foreach (var package in BrowserPackages) {
                var extras = new Bundle();
                extras.PutString(Intent.ExtraText, _reportUri.ToString());
                replacements.PutBundle(package, extras);
            }
            chooser.PutExtra(Intent.ExtraReplacementExtras, replacements);

            _activity.StartActivity(chooser);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Failed to share the report.");
        }
    }

    private string ReportFileName()
    {
        var name = Path.GetFileName(_reportUri.LocalPath);
        return string.IsNullOrEmpty(name) ? "report.txt" : name;
    }

    private ImageButton IconButton(int drawableRes)
    {
        var button = new ImageButton(_activity);
        button.SetImageResource(drawableRes);
        button.SetBackgroundColor(Android.Graphics.Color.Transparent);
        var size = Dp(40);
        button.LayoutParameters = new LinearLayout.LayoutParams(size, size);
        return button;
    }

    private int Dp(int dp) => (int)(dp * _activity.Resources!.DisplayMetrics!.Density);

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    // Bridges the WebView's native find callback to the match-count label.
    private sealed class FindListener(Action<int, int> onResult) : Java.Lang.Object, WebView.IFindListener
    {
        public void OnFindResultReceived(int activeMatchOrdinal, int numberOfMatches, bool isDoneCounting)
        {
            if (isDoneCounting)
                onResult(activeMatchOrdinal, numberOfMatches);
        }
    }
}
