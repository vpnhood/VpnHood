using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using VpnHood.AppUi.Hosting.WebView.Windows.WinNative;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// A window WebView2 draws a page in, as WPF's WebView2 control did: the controller made for it
// (CreateController), kept the size of its client area, rendering only while the window shows,
// handed the window's focus, and closed with the window. What else the window's owner wants of a
// message, its own handler does.
internal sealed class WebView2Window
{
    private const uint WmDestroy = 0x0002;
    private const uint WmMove = 0x0003;
    private const uint WmSize = 0x0005;
    private const uint WmSetFocus = 0x0007;
    private const uint WmShowWindow = 0x0018;
    private const int SizeMinimized = 1;
    private readonly VhColor? _background;
    private readonly Win32Window.MessageHandler? _onMessage;
    private CoreWebView2Controller? _controller;

    public WebView2Window(string title, uint style, IntPtr owner, VhColor? background,
        Win32Window.MessageHandler? onMessage)
    {
        _background = background;
        _onMessage = onMessage;
        Window = new Win32Window(title, style, owner, background, OnMessage);
    }

    public Win32Window Window { get; }

    // On the window's thread, whose loop WebView2 answers through.
    public async Task<CoreWebView2Controller> CreateController(CoreWebView2Environment environment)
    {
        var controller = await environment.CreateCoreWebView2ControllerAsync(Window.Handle);

        // the window's colour until the page paints, not WebView2's white
        if (_background is { } color)
            controller.DefaultBackgroundColor = Color.FromArgb(255, color.R, color.G, color.B);

        // a Tab past the page's last control comes back to its first: the window has nothing else
        controller.MoveFocusRequested += (_, e) => {
            e.Handled = true;
            controller.MoveFocus(e.Reason);
        };

        controller.Bounds = ToRectangle(Window.ClientRect);
        controller.IsVisible = Window.IsVisible && !Window.IsMinimized;
        _controller = controller;

        // the focus the window got before the page was there to take it
        if (GetFocus() == Window.Handle)
            controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);

        return controller;
    }

    private IntPtr? OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_controller is { } controller) {
            switch (msg) {
                case WmSize:
                    controller.IsVisible = wParam != SizeMinimized && Window.IsVisible;
                    if (wParam != SizeMinimized)
                        controller.Bounds = ToRectangle(Window.ClientRect);
                    break;

                case WmShowWindow:
                    controller.IsVisible = wParam != 0 && !Window.IsMinimized;
                    break;

                // where the page's own popups - a list, the IME - open
                case WmMove:
                    controller.NotifyParentWindowPositionChanged();
                    break;

                // The keyboard is the page's, the window's whole content. Windows gives the focus to
                // the window itself on every activation, so the page's focus would show no ring and
                // take no key until a click or a Tab - on the TV UI, a remote it ignores (owner,
                // 2026-09-13).
                case WmSetFocus:
                    controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                    break;

                case WmDestroy:
                    controller.Close();
                    _controller = null;
                    break;
            }
        }

        return _onMessage?.Invoke(msg, wParam, lParam);
    }

    private static Rectangle ToRectangle(Win32Rect clientRect)
    {
        return new Rectangle(0, 0, clientRect.Width, clientRect.Height);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
}
