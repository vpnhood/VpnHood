using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace VpnHood.AppUi.SmokeTest;

// The window as a test can touch it. Windows publishes every Avalonia control through UI
// Automation, so the tree a screen reader would read is the tree a walk drives, and a control is
// addressed by its x:Name, which Avalonia reports as the automation id. Nothing here knows a page.
internal sealed class UiDriver(Process process) : IDisposable
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(150);

    // How long a control may take to appear. A page draws after the click that asked for it, and a
    // first draw on a cold process is slower than any later one.
    public TimeSpan FindTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public AutomationElement Root {
        get {
            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("The head has no window; it may have closed.");
            return AutomationElement.FromHandle(handle);
        }
    }

    public static UiDriver Start(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(executablePath) {
            WorkingDirectory = Path.GetDirectoryName(executablePath)
                               ?? throw new ArgumentException("The head has no folder.", nameof(executablePath)),
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException($"Could not start {executablePath}.");

        var driver = new UiDriver(process);
        try {
            driver.WaitForWindow(timeout);
            return driver;
        }
        catch {
            // a head that never showed a window must not be left running behind the failure
            driver.Dispose();
            throw;
        }
    }

    // An on-screen control by its x:Name, or by the words it shows when it has no name of its own.
    public AutomationElement Find(string name)
    {
        return TryFind(name, FindTimeout)
               ?? throw new InvalidOperationException($"No on-screen control '{name}'. Page: '{PageTitle ?? "(none)"}'.");
    }

    public AutomationElement? TryFind(string name)
    {
        return TryFind(name, TimeSpan.Zero);
    }

    public AutomationElement? TryFind(string name, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        do {
            var hit = FindOnce(name);
            if (hit != null)
                return hit;
            Thread.Sleep(PollDelay);
        } while (DateTime.UtcNow < until);

        return null;
    }

    // A card carries its name on a container whose Button is a CHILD, while a label's button is an
    // ANCESTOR; a finger resolves both without thinking, so the walk does too. Only Invoke is
    // honoured, never Toggle: a walk reads the app, it does not change its settings.
    public void Invoke(string name)
    {
        var element = Find(name);
        if (TryInvoke(element))
            return;

        var inside = element.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.IsInvokePatternAvailableProperty, true));
        if (inside != null && TryInvoke(inside))
            return;

        var walker = TreeWalker.ControlViewWalker;
        for (var node = walker.GetParent(element); node != null; node = walker.GetParent(node)) {
            if (TryInvoke(node))
                return;
        }

        throw new InvalidOperationException($"Found '{name}' but nothing on its branch could be invoked.");
    }

    // What the page calls itself: a page header's own block, or the title a feature page draws.
    // Null on the home, which wears the app's name rather than a title.
    public string? PageTitle {
        get {
            var element = TryFind("TitleBlock") ?? TryFind("TitleText");
            var title = element?.Current.Name;
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
    }

    // The error dialog is the one place that names its sentence MessageText.
    public string? ErrorText => TryFind("MessageText")?.Current.Name;

    // The title once it is no longer the one the click started from. A page draws after the click
    // that asked for it, so the first read is usually still the old page; null means it never
    // changed, which is a page that did not open or one that re-opened itself.
    public string? WaitForPageTitleChange(string? previous, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        do {
            var title = PageTitle;
            if (title != previous)
                return title;
            Thread.Sleep(PollDelay);
        } while (DateTime.UtcNow < until);

        return null;
    }

    // Back until no page header is left, which is the home. A page that will not leave is a defect
    // of its own, so this says so rather than looping for ever.
    public void ReturnHome()
    {
        for (var depth = 0; depth < 12; depth++) {
            // a dialog first: it sits over the page and swallows the page's own back
            if (TryFind("CloseButton") != null) {
                Invoke("CloseButton");
                Thread.Sleep(PollDelay);
                continue;
            }

            // The drawer next. It disables the page behind it, the menu button that opened it
            // included, so the way out is the drawer's own item: it navigates and closes the
            // drawer in one act, and the page it opens is popped on the next turn of this loop.
            if (TryFind("SettingsItem") != null) {
                Invoke("SettingsItem");
                Thread.Sleep(PollDelay);
                continue;
            }

            if (TryFind("BackButton") == null)
                return;

            Invoke("BackButton");
            Thread.Sleep(PollDelay);
        }

        throw new InvalidOperationException($"The walk could not get back to the home page. Page: '{PageTitle ?? "(none)"}'.");
    }

    // Best effort, and never a verdict: a picture is for a person to look at after the run. A
    // failure to take one is said out loud but changes no result.
    public void TrySaveScreenshot(string path)
    {
        try {
            var bounds = Root.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
                return;

            // the window in front, so the copy sees it rather than whatever covers it
            SetForegroundWindow(process.MainWindowHandle);
            Thread.Sleep(PollDelay);

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var bitmap = new Bitmap((int)bounds.Width, (int)bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, bitmap.Size);
            bitmap.Save(path, ImageFormat.Png);
        }
        catch (Exception ex) {
            Console.WriteLine($"Could not save the picture '{path}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        try {
            process.Refresh();
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(milliseconds: 10_000);
        }
        catch (InvalidOperationException) {
            // already gone, which is the state this wanted
        }
        finally {
            process.Dispose();
        }
    }

    private void WaitForWindow(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until) {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The head exited with code {process.ExitCode} before it opened a window.");

            if (process.MainWindowHandle != IntPtr.Zero)
                return;

            Thread.Sleep(PollDelay);
        }

        throw new TimeoutException($"The head opened no window within {timeout}.");
    }

    // A collapsed control is still in Avalonia's tree and still says it is on screen, so its size
    // is what separates what a person can touch from what is only there: a hidden row, the closed
    // drawer's items, the statistics a disconnected home does not draw, all measure zero.
    // Asking for the whole tree while a page is still drawing itself can fail outright: the tree
    // changed under the question. That is a moment, not an answer, so it is asked again.
    private AutomationElement? FindOnce(string name)
    {
        for (var attempt = 0; attempt < 3; attempt++) {
            try {
                var onScreen = Root
                    .FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .Where(IsVisible)
                    .ToArray();

                return onScreen.FirstOrDefault(x => x.Current.AutomationId == name)
                       ?? onScreen.FirstOrDefault(x => x.Current.Name == name);
            }
            catch (COMException) {
                Thread.Sleep(PollDelay);
            }
            catch (ElementNotAvailableException) {
                Thread.Sleep(PollDelay);
            }
        }

        return null;
    }

    private static bool IsVisible(AutomationElement element)
    {
        try {
            var current = element.Current;
            return !current.IsOffscreen
                   && current.BoundingRectangle.Width >= 1
                   && current.BoundingRectangle.Height >= 1;
        }
        catch (ElementNotAvailableException) {
            // it went away between the search and the question, so it is not there to touch
            return false;
        }
        catch (COMException) {
            return false;
        }
    }

    private static bool TryInvoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            return false;

        try {
            ((InvokePattern)pattern).Invoke();
            return true;
        }
        catch (Exception) {
            // The peer refused it. Avalonia raises a bare error for a control that cannot take a
            // click after all, which reads the same as having no pattern: the caller keeps looking.
            return false;
        }
    }

    // the plain import on purpose: the generated one is unsafe code, and this is one call
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
