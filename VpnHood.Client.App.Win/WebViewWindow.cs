using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VpnHood.Client.App
{
    public class WebViewWindow
    {
        public Form Form { get; }
        private Size DefWindowSize = new Size(400, 700);

        public static bool IsInstalled
        {
            get
            {
                return Environment.Is64BitOperatingSystem
                    ? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv", null) != null
                    : Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv", null) != null;
            }
        }

        public WebViewWindow(string url, string dataFolderPath)
        {

            Form = new Form
            {
                AutoScaleMode = AutoScaleMode.Font,
                ClientSize = DefWindowSize,
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
                Size = DefWindowSize,
                Parent = Form
            };

            webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
            var _ = InitWebViewUrl(webView, url, dataFolderPath);


            Form.Controls.Add(webView);
            DefWindowSize = new Size(DefWindowSize.Width * (Form.DeviceDpi / 96), DefWindowSize.Height * (Form.DeviceDpi / 96));
        }

        private static async Task InitWebViewUrl(WebView2 webView, string url, string dataFolderPath)
        {
            var objCoreWebView2Environment = await CoreWebView2Environment.CreateAsync(null, dataFolderPath, null);
            await webView.EnsureCoreWebView2Async(objCoreWebView2Environment);
            webView.Source = new Uri(url);
        }

        public void Show()
        {
            MethodInvoker methodInvokerDelegate = delegate () { Show(); };
            if (Form.InvokeRequired)
            {
                Form.Invoke(methodInvokerDelegate);
                return;
            }

            // body
            var rect = Screen.PrimaryScreen.WorkingArea;
            var size = DefWindowSize;

            Form.Location = new Point(rect.Right - size.Width, rect.Bottom - size.Height);
            Form.Size = size;
            //if (rect.Left > 0) Form.Location = new Point(rect.Left, rect.Bottom - size.Height);
            //if (rect.Top > 0) Form.Location = new Point(rect.Right - size.Width, rect.Top);

            Form.Show();
            Form.BringToFront();
            Form.Focus();
            Form.Activate();

        }

        private void WebView_CoreWebView2InitializationCompleted(object sender, EventArgs e)
        {
            var webView = (WebView2)sender;
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = e.Uri,
                UseShellExecute = true,
                Verb = "open"
            });
            e.Handled = true;
        }

        private void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ((Form)sender).Visible = false;
            }
        }

        private void Form_Deactivate(object sender, EventArgs e)
        {
            ((Form)sender).Visible = false;
        }


    }
}
