using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

namespace VpnHood.App.StoreScreenshots;

// One shot, on the UI thread: the page is opened, given time to arrive and settle, checked for
// anything that must not be in a store screenshot, and captured. The checks are the web UI
// engine's, made for this toolkit: a dialog over the page (an error the page caught and showed),
// an error the UI logged, an exception the dispatcher caught - and a settle that never comes.
internal sealed class ShotRunner(ShotOptions options, Window window, MainView view, CapturingLogger log)
{
    private static readonly TimeSpan SettleStep = TimeSpan.FromMilliseconds(200);
    private readonly List<string> _dispatcherErrors = [];

    public async Task<int> RunAsync()
    {
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        try {
            await CaptureAsync();
            Report();
            return 0;
        }
        catch (Exception ex) {
            Report();
            Console.Error.WriteLine($"FAILED    {ex.Message}");
            return 1;
        }
    }

    private async Task CaptureAsync()
    {
        // the device's pixel ratio, times the engine's supersampling: what the frame pass expects
        window.SetRenderScaling(options.Scale);
        await Frames(2);

        if (!PageRoutes.IsHome(options.Route)) {
            PageRoutes.Open(options.Route, view);
            Console.WriteLine($"opened    {options.Route}");
        }

        // the page transition, the images the page asked for, the first state tick: the capture
        // waits until two frames in a row are the same, or says it could not
        Freeze();
        var settled = await WaitForStillFrames();
        if (!settled)
            Console.WriteLine($"warning   the page kept changing for {options.SettleTimeout.TotalSeconds:0}s; captured anyway");

        // a row the state tick rebuilt while the page settled comes back with its class
        Freeze();

        foreach (var path in options.Hide)
            Hide(path);
        if (options.Hide.Count > 0)
            await Frames(2);

        FailOnOverlay();
        FailOnErrors();

        var frame = Frame();
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? throw new InvalidOperationException($"--out has no folder: {options.OutputPath}"));
        frame.Save(options.OutputPath, new PngBitmapEncoderOptions());
        Console.WriteLine($"captured  {Path.GetFileName(options.OutputPath)}  {frame.PixelSize.Width}x{frame.PixelSize.Height}  {options.Route}");
    }

    private async Task<bool> WaitForStillFrames()
    {
        var clock = Stopwatch.StartNew();
        byte[]? previous = null;
        while (clock.Elapsed < options.SettleTimeout) {
            await Task.Delay(SettleStep);
            var pixels = Pixels(Frame());
            if (previous != null && pixels.AsSpan().SequenceEqual(previous))
                return true;
            previous = pixels;
        }

        return false;
    }

    // What the window has drawn. A headless top level answers nothing until it is shown and a
    // frame has been rendered, and a capture of nothing must not pass for an empty screenshot.
    private WriteableBitmap Frame()
    {
        return window.CaptureRenderedFrame()
               ?? throw new InvalidOperationException("The window has rendered no frame to capture.");
    }

    private static byte[] Pixels(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    // a couple of render ticks, for a change that needs no waiting
    private static async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(16);
        }
    }

    // Takes the never-ending animations off the page (see ShotOptions.Freeze). Unlike a hide, a
    // class that is not there is no error: most pages carry none of them.
    private void Freeze()
    {
        foreach (var control in view.GetVisualDescendants().OfType<Control>())
        foreach (var name in options.Freeze)
            control.Classes.Remove(name);
    }

    // Fails rather than quietly ships the element: a renamed control would otherwise put a hidden
    // chip back into the listing (the web UI engine's rule for its selectors).
    private void Hide(string path)
    {
        Control scope = view;
        foreach (var name in path.Split('/'))
            scope = scope.GetVisualDescendants().OfType<Control>().FirstOrDefault(x => x.Name == name)
                    ?? throw new InvalidOperationException($"Nothing named '{name}' was found under '{path}', so it was not hidden.");
        scope.IsVisible = false;
        Console.WriteLine($"hidden    {path}");
    }

    // No store screenshot is meant to have a dialog open: that is an error the page caught and
    // showed, a review prompt, or anything added later. A snackbar or an update notice is the same
    // kind of thing.
    private void FailOnOverlay()
    {
        foreach (var name in new[] { "DialogLayer", "Snackbar", "UpdateNotice" }) {
            var layer = view.GetVisualDescendants().OfType<Control>().FirstOrDefault(x => x.Name == name);
            if (layer is { IsVisible: true })
                throw new InvalidOperationException($"{name} is showing over the page: \"{TextOf(layer)}\"" + UnmockedHint());
        }
    }

    private void FailOnErrors()
    {
        var errors = log.Errors.Concat(_dispatcherErrors).Distinct().ToArray();
        if (errors.Length == 0)
            return;
        throw new InvalidOperationException("the UI reported an error while rendering:\n  " + string.Join("\n  ", errors.Take(5)) + UnmockedHint());
    }

    private static string UnmockedHint()
    {
        var unmocked = UnmockedCalls.All;
        return unmocked.Count == 0
            ? ""
            : "\n  unmocked API calls during this shot (a likely cause - answer them in the fake, or add the fixture field they stand in for): " + string.Join(", ", unmocked);
    }

    private static string TextOf(Control control)
    {
        var text = string.Join(" ", control.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)));
        return text.Length > 300 ? text[..300] : text;
    }

    private void Report()
    {
        var unmocked = UnmockedCalls.All;
        if (unmocked.Count > 0)
            Console.WriteLine($"unmocked  {string.Join(", ", unmocked)}");
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _dispatcherErrors.Add($"{e.Exception.GetType().Name}: {e.Exception.Message}");
        e.Handled = true;
    }
}
