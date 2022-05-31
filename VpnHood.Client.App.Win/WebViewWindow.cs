using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace VpnHood.Client.App;

public class WebViewWindow
{
    private readonly Size _defWindowSize = new(400, 700);

    public WebViewWindow(string url, string dataFolderPath)
    {
        Form = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = _defWindowSize,
            Visible = false,
            ShowInTaskbar = false,
            Icon = Resource.VpnHoodIcon,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None
        };
        Form.FormClosing += Form_FormClosing;
        Form.Deactivate += Form_Deactivate;

        var webView = new WebView2
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Size = _defWindowSize,
            Parent = Form
        };

        webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        var _ = InitWebViewUrl(webView, url, dataFolderPath);


        Form.Controls.Add(webView);
        _defWindowSize = new Size(_defWindowSize.Width * (Form.DeviceDpi / 96),
            _defWindowSize.Height * (Form.DeviceDpi / 96));
    }

    public Form Form { get; }

    public static bool IsInstalled =>
        Environment.Is64BitOperatingSystem
            ? Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                "pv", null) != null
            : Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                "pv", null) != null;

    private static async Task InitWebViewUrl(WebView2 webView, string url, string dataFolderPath)
    {
        var objCoreWebView2Environment = await CoreWebView2Environment.CreateAsync(null, dataFolderPath);
        await webView.EnsureCoreWebView2Async(objCoreWebView2Environment);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.Source = new Uri(url);
    }

    public void Show()
    {
        if (Form.InvokeRequired)
        {
            void MethodInvokerDelegate() { Show(); }
            Form.Invoke((MethodInvoker) MethodInvokerDelegate);
            return;
        }

        // body
        var rect = Screen.PrimaryScreen.WorkingArea;
        var size = _defWindowSize;

        Form.Size = size;
        Form.Location = new Point(rect.Right - size.Width, rect.Bottom - size.Height);
        if (rect.Top > 10) Form.Location = new Point(rect.Right - size.Width, rect.Top);
        if (rect.Left > 10) Form.Location = new Point(rect.Left, rect.Bottom - size.Height);

        Form.Show();
        Form.BringToFront();
        Form.Focus();
        Form.Activate();
    }

    private void WebView_CoreWebView2InitializationCompleted(object? sender, EventArgs e)
    {
        if (sender is WebView2 webView)
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri,
            UseShellExecute = true,
            Verb = "open"
        });
        e.Handled = true;
    }

    private void Form_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (sender is Form form)
                form.Visible = false;
        }
    }

    private void Form_Deactivate(object? sender, EventArgs e)
    {
        if (sender is Form form)
            form.Visible = false;
    }
}