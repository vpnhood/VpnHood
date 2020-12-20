using VpnHood.Client.App.UI;
using System;
using System.Diagnostics;
using System.Windows.Forms;
using VpnHood.Common;
using VpnHood.Logging;
using System.Threading;
using Microsoft.Extensions.Logging;
using System.IO;

namespace VpnHood.Client.App
{
    internal class App : IDisposable
    {
        private static readonly Mutex _mutex = new Mutex(false, typeof(Program).FullName);
        private NotifyIcon _notifyIcon;
        private VpnHoodApp _app;
        private VpnHoodAppUI _appUI;
        private bool _isDisposed;
        private static readonly AppUpdater _appUpdater = new AppUpdater();

        public App()
        {
        }

        public void Start(bool openWindow, bool logToConsole)
        {
            // Report current Version
            // Replace dot in version to prevent anonymouizer treat it as ip.
            VhLogger.Current.LogInformation($"{typeof(App).Assembly.ToString().Replace('.', ',')}, Time: {DateTime.Now}");

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(0), false))
            {
                VhLogger.Current.LogInformation($"{nameof(App)} is already running!");
                return;
            }

            // check update
            _appUpdater.Updated += (sender, e) => Exit();
            _appUpdater.Start();
            if (_appUpdater.IsUpdated)
            {
                _appUpdater.LaunchUpdated();
                return;
            }

            // init app
            _app = VpnHoodApp.Init(new WinAppProvider(), new AppOptions() { LogToConsole = logToConsole });
            _appUI = VpnHoodAppUI.Init(new MemoryStream(Resource.SPA));

            // create notification icon
            InitNotifyIcon();

            // MainWindow
            if (openWindow)
                OpenMainWindow();

            // Message Loop
            Application.Run();
        }

        public void OpenMainWindow()
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = _appUI.Url,
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
            _notifyIcon.Text = "VpnHood";
            _notifyIcon.Visible = true;

        }

        public void Exit()
        {
            if (_isDisposed)
                return;

            Dispose();
            Application.Exit();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            _notifyIcon?.Dispose();
            _appUI?.Dispose();
            _app?.Dispose();

            // update
            if (_appUpdater.IsUpdated)
                _appUpdater.LaunchUpdated(new string[] { "/nowindow" });
            _appUpdater?.Dispose();
        }
    }
}
