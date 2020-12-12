using VpnHood.Client.App.UI;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace VpnHood.Client.App
{
    internal class App : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private VpnHoodApp _app;
        private VpnHoodAppUI _appUI;
        private bool _disposed;

        public App()
        {
        }

        public void Init(bool logToConsole)
        {
            // init app
            _app = VpnHoodApp.Init(new WinAppProvider(), new AppOptions() { LogToConsole = logToConsole });
            _appUI = VpnHoodAppUI.Init();
            _appUI.Start();

            // create notification icon on browser mode
            InitNotifyIcon();

            OpenMainWindow();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
        public void Run() => Application.Run();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
        public void OpenMainWindow()
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = VpnHoodAppUI.Current.Url,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void InitNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = Resource.VpnHoodIcon
            };
            _notifyIcon.MouseClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenMainWindow();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(AppUIResource.Open, null, (sender, e) => OpenMainWindow());
            menu.Items.Add(AppUIResource.Exit, null, (sender, e) => Exit());
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Text = "VPN";
            _notifyIcon.Visible = true;

        }

        public void Exit()
        {
            if (_disposed)
                return;

            Dispose();
            Application.Exit();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _notifyIcon?.Dispose();
            _appUI?.Dispose();
            _app?.Dispose();
        }
    }
}
