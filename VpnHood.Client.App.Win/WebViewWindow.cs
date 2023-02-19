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
    private readonly Color _backColor = Color.FromArgb(34, 67, 166);
    private readonly Size _defWindowSize = new(400, 700);
    private readonly WebView2 _webView = new();
    
    public bool IsInitCompleted { get; private set; }
    public event EventHandler? InitCompleted;
    public Form Form { get; }

    public WebViewWindow(Uri url, string dataFolderPath)
    {
        Form = new Form
        {
            // ReSharper disable once LocalizableElement
            Text = "VpnHood!", //required to be the main app
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = _defWindowSize,
            Visible = false,
            ShowInTaskbar = false,
            Icon = Resource.VpnHoodIcon,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None,
            TopLevel = true,
            BackColor = _backColor
        };
        Form.FormClosing += Form_FormClosing;
        Form.FormClosed += Form_FormClosed;
        Form.Deactivate += Form_Deactivate;

        InitWebView(url, dataFolderPath);

        _defWindowSize = new Size(_defWindowSize.Width * (Form.DeviceDpi / 96),
        _defWindowSize.Height * (Form.DeviceDpi / 96));
        UpdatePosition();
    }

    public static bool IsInstalled =>
        Environment.Is64BitOperatingSystem
            ? Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                "pv", null) != null
            : Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                "pv", null) != null;

    private void InitWebView(Uri url, string dataFolderPath)
    {
        ((System.ComponentModel.ISupportInitialize)(_webView)).BeginInit();
        _webView.CreationProperties = new CoreWebView2CreationProperties() { UserDataFolder = dataFolderPath };
        _webView.Source = url;
        _webView.Anchor = AnchorStyles.Bottom | AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _webView.Size = _defWindowSize;
        _webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        _webView.BackColor = _backColor;
        _webView.DefaultBackgroundColor = _backColor;
        Form.Controls.Add(_webView);
        ((System.ComponentModel.ISupportInitialize)(_webView)).EndInit();
    }

    public Task EnsureCoreWebView2Async()
    {
        return _webView.EnsureCoreWebView2Async();
    }

    private void UpdatePosition()
    {
        // body
        var rect = Screen.PrimaryScreen?.WorkingArea ?? throw new Exception("Could not get the size of Screen.PrimaryScreen");
        var size = _defWindowSize;

        Form.Size = size;
        Form.Location = new Point(rect.Right - size.Width, rect.Bottom - size.Height);
        if (rect.Top > 10) Form.Location = new Point(rect.Right - size.Width, rect.Top);
        if (rect.Left > 10) Form.Location = new Point(rect.Left, rect.Bottom - size.Height);
    }

    public void Show()
    {
        if (Form.InvokeRequired)
        {
            void MethodInvokerDelegate() { Show(); }
            Form.Invoke((MethodInvoker)MethodInvokerDelegate);
            return;
        }

        UpdatePosition();
        Form.Show();
        Form.BringToFront();
        Form.Focus();
        Form.Activate();
    }

    private void WebView_CoreWebView2InitializationCompleted(object? sender, EventArgs e)
    {
        if (sender is WebView2 webView)
        {
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            IsInitCompleted = true;
            InitCompleted?.Invoke(this, EventArgs.Empty);
        }
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

    private void Form_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _webView.Dispose();
    }

    public void Close()
    {
        Form.Close();
        _webView.Dispose();
    }
}