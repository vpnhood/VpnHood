using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AppUi.SmokeTest;

// The page a paired phone opens, the way a phone opens it: the head's web host at its LAN address, over plain http,
// which a browser does not count as a secure context. Only a browser at that address shows whether the page starts
// there (.NET 11's runtime does not by itself: the page's subtle-digest.js). See README.md in this folder.
[TestClass]
[TestCategory(UiPageWalkTest.Category)]
[DoNotParallelize]
public class BrowserPageTest
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);

    // Edge opens the tab on about:blank before the page. main.js takes the notice away once the UI is on the page, and
    // marks it failed with the error otherwise.
    private const string PageState =
        """
        (() => {
            if (location.protocol !== 'http:' || document.readyState !== 'complete')
                return 'loading';
            if (!document.getElementById('out'))
                return 'no-page';
            const notice = document.getElementById('loading');
            if (!notice)
                return 'started';
            return notice.classList.contains('failed') ? 'failed: ' + notice.textContent : 'loading';
        })()
        """;

    [TestMethod]
    public async Task PageStartsOverPlainHttp()
    {
        if (!Environment.UserInteractive)
            Assert.Inconclusive("The head opens a window, which needs an interactive desktop.");

        var edgePath = HeadlessEdge.FindExecutable()
                       ?? throw new AssertInconclusiveException("Microsoft Edge is not installed.");

        using var cancellationSource = new CancellationTokenSource(StartTimeout * 2);
        using var session = DevHeadSession.Start();
        var pageUrl = await WaitForPageUrl(session, cancellationSource.Token);
        await using var edge = await HeadlessEdge.StartAsync(edgePath, pageUrl, cancellationSource.Token);

        var state = await WaitForPageState(edge, cancellationSource.Token);
        if (state == "no-page")
            Assert.Inconclusive($"The head serves no page at {pageUrl}: run " +
                                "src/libs/AppUi/VpnHood.AppUi.Avalonia.Classic.Browser/_publish.ps1, then build tools/AvaloniaUI.Dev.");

        var isSecureContext = await edge.EvaluateAsync("globalThis.isSecureContext", cancellationSource.Token);
        Assert.IsFalse(isSecureContext.GetBoolean(),
            $"{pageUrl} counts as a secure context, so this run cannot show the page starting without one.");

        Assert.AreEqual("started", state, $"The page at {pageUrl} did not start.");
    }

    // where the head's web host listens, from its log: the one place that names the address it chose
    private static async Task<Uri> WaitForPageUrl(DevHeadSession session, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30)) {
            var match = Regex.Match(session.ReadLog(), @"remote web server listener on (?<endpoint>\S+?)\.{3}");
            if (match.Success) {
                var endPoint = IPEndPoint.Parse(match.Groups["endpoint"].Value);
                if (IPAddress.IsLoopback(endPoint.Address))
                    Assert.Inconclusive("The head listens on loopback alone, a secure context: this machine has no LAN address.");

                var host = endPoint.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{endPoint.Address}]" : $"{endPoint.Address}";
                return new Uri($"http://{host}:{endPoint.Port}/");
            }

            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException($"The head's log names no web host listener. See {session.LogPath}");
    }

    private static async Task<string?> WaitForPageState(HeadlessEdge edge, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < StartTimeout) {
            var state = await edge.EvaluateAsync(PageState, cancellationToken);
            if (state.GetString() != "loading")
                return state.GetString();

            await Task.Delay(500, cancellationToken);
        }

        return "loading";
    }
}
