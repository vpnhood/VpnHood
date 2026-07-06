using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using WebKit;

namespace VpnHood.AppLib.Ios.Common.SpaWebView;

// Handles the target="_blank"/window.open links the SPA opens (the "open log" button and the error dialog's
// "Open Report"). Loopback URLs point at the app's own on-device report server, which external Safari can't
// reach (unreachable cross-process, and torn down the moment the app backgrounds), so the resource is fetched
// in-app and shown in a modal viewer: the user reads the log inline, searches it with the native find bar, and
// can Save-to-Files/AirDrop/Mail it via the Share button. Anything non-loopback is a real external link and is
// handed to the system browser.
internal sealed class IosReportViewer(UIViewController hostController, UIColor backgroundColor)
{
    public void HandleNewWindow(NSUrl? url)
    {
        if (url?.AbsoluteString is not { } urlString || !Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            return;

        if (!uri.IsLoopback) {
            UIApplication.SharedApplication.OpenUrl(url, new NSDictionary(), null);
            return;
        }

        _ = DownloadAndViewAsync(uri);
    }

    private async Task DownloadAndViewAsync(Uri uri)
    {
        try {
            using var httpClient = new HttpClient();
            var content = await httpClient.GetByteArrayAsync(uri);

            // Keep the served resource's extension (e.g. log.txt) so the viewer renders it as text and the
            // exported copy is treated as a text file by Files/Mail.
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = "report.txt";
            var filePath = Path.Combine(Path.GetTempPath(), "VpnHood-" + fileName);
            await File.WriteAllBytesAsync(filePath, content);

            hostController.BeginInvokeOnMainThread(() => PresentReportViewer(filePath));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Failed to download the report for viewing.");
        }
    }

    // Show the downloaded report in a modal WKWebView (text/plain renders inline) inside a navigation
    // controller, with a Search button (in-app find), a Share button (native iOS share sheet) and a Done button
    // to dismiss. This runs in the app process (no extension jetsam budget), so a second web view is fine.
    private void PresentReportViewer(string filePath)
    {
        var viewer = new UIViewController { Title = Path.GetFileName(filePath) };
        var view = viewer.View!;
        view.BackgroundColor = backgroundColor;

        var webView = new WKWebView(CGRect.Empty, new WKWebViewConfiguration()) {
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        // Native Safari-style find-on-page (iOS 16+). External Safari can't search the log because it lives on
        // the app's loopback report server, so we give the same find bar in-app instead.
        if (OperatingSystem.IsIOSVersionAtLeast(16))
            webView.FindInteractionEnabled = true;

        view.AddSubview(webView);
        NSLayoutConstraint.ActivateConstraints([
            webView.TopAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.TopAnchor),
            webView.BottomAnchor.ConstraintEqualTo(view.BottomAnchor),
            webView.LeadingAnchor.ConstraintEqualTo(view.LeadingAnchor),
            webView.TrailingAnchor.ConstraintEqualTo(view.TrailingAnchor)
        ]);

        var fileUrl = NSUrl.FromFilename(filePath);
        webView.LoadFileUrl(fileUrl, NSUrl.FromFilename(Path.GetDirectoryName(filePath)!));

        var nav = new UINavigationController(viewer);

        var shareButton = new UIBarButtonItem(UIBarButtonSystemItem.Action);
        shareButton.Clicked += (_, _) => PresentShareSheet(filePath, nav, shareButton);

        // Show a Search button that opens the find bar, alongside Share. (First item sits at the far right.)
        if (OperatingSystem.IsIOSVersionAtLeast(16)) {
            var searchButton = new UIBarButtonItem(UIBarButtonSystemItem.Search);
            searchButton.Clicked += (_, _) => webView.FindInteraction?.PresentFindNavigatorShowingReplace(false);
            viewer.NavigationItem.RightBarButtonItems = [shareButton, searchButton];
        }
        else {
            viewer.NavigationItem.RightBarButtonItem = shareButton;
        }

        var doneButton = new UIBarButtonItem(UIBarButtonSystemItem.Done);
        doneButton.Clicked += (_, _) => nav.DismissViewController(true, null);
        viewer.NavigationItem.LeftBarButtonItem = doneButton;

        // Present from the host controller; the share sheet is later presented from this nav (the top-most VC).
        hostController.PresentViewController(nav, animated: true, completionHandler: null);
    }

    private static void PresentShareSheet(string filePath, UIViewController presenter, UIBarButtonItem anchor)
    {
        var fileUrl = NSUrl.FromFilename(filePath);
        var activityController = new UIActivityViewController([fileUrl], applicationActivities: null);

        // iPad requires the share sheet's popover to be anchored — pin it to the Share bar button item.
        if (activityController.PopoverPresentationController is { } popover)
            popover.BarButtonItem = anchor;

        presenter.PresentViewController(activityController, animated: true, completionHandler: null);
    }
}
