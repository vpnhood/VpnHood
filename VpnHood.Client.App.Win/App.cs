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
    internal class App : ApplicationContext
    {
        private bool _disposed = false;
        private static readonly Mutex _mutex = new Mutex(false, typeof(Program).FullName);
        private NotifyIcon _notifyIcon;
        private VpnHoodApp _app;
        private VpnHoodAppUI _appUI;
        private static readonly AppUpdater _appUpdater = new AppUpdater();
        private WebViewWindow _webViewWindow;
        private FileSystemWatcher _fileSystemWatcher;

        public App()
        {
        }

        public void Start(bool openWindow, bool logToConsole)
        {
            // Report current Version
            // Replace dot in version to prevent anonymouizer treat it as ip.
            VhLogger.Current = VhLogger.CreateConsoleLogger();
            VhLogger.Current.LogInformation($"{typeof(App).Assembly.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            var appDataPath = new AppOptions().AppDataPath; // we use defaultPath
            var appCommandFilePath = Path.Combine(appDataPath, "appCommand.txt");

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(0), false))
            {
                // open main window if app is already running and user run the app again
                File.WriteAllText(appCommandFilePath, "OpenMainWindow");
                VhLogger.Current.LogInformation($"{nameof(App)} is already running!");
                return;
            }

            // check update
            _appUpdater.Updated += (sender, e) => Application.Exit();
            _appUpdater.Start();
            if (_appUpdater.IsUpdated)
            {
                _appUpdater.LaunchUpdated();
                return;
            }

            // configuring Windows Firewall
            try
            {
                OpenLocalFirewall(appDataPath);
            }
            catch { };

            // init app
            _app = VpnHoodApp.Init(new WinAppProvider(), new AppOptions() { LogToConsole = logToConsole });
            _appUI = VpnHoodAppUI.Init(new MemoryStream(Resource.SPA));

            // create notification icon
            InitNotifyIcon();

            // Create webview if installed
            if (WebViewWindow.IsInstalled)
                _webViewWindow = new WebViewWindow(_appUI.Url);

            // MainWindow
            if (openWindow)
                OpenMainWindow();

            // Init command watcher for external command
            InitCommnadWatcher(appCommandFilePath);

            // Message Loop
            if (_webViewWindow != null)
                Application.Run(_webViewWindow.Form);
            else
                Application.Run(this);
        }

        private void InitCommnadWatcher(string path)
        {
            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(path),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(path),
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileSystemWatcher.Changed += (sender, e) =>
            {
                try
                {
                    Thread.Sleep(100);
                    var cmd = File.ReadAllText(e.FullPath);
                    if (cmd == "OpenMainWindow")
                        OpenMainWindow();
                }
                catch { }
            };
        }

        public void OpenMainWindow(string url = null)
        {
            if (_webViewWindow != null)
            {
                _webViewWindow.Show();
                return;
            }

            Process.Start(new ProcessStartInfo()
            {
                FileName = url ?? _appUI.Url,
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
            menu.Items.Add(AppUIResource.Exit, null, (sender, e) => Application.Exit());
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Text = "VpnHood";
            _notifyIcon.Visible = true;

        }
        private static string FindExePath(string exe)
        {
            exe = Environment.ExpandEnvironmentVariables(exe);
            if (File.Exists(exe))
                return Path.GetFullPath(exe);

            if (Path.GetDirectoryName(exe) == string.Empty)
            {
                foreach (string test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    string path = test.Trim();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path = Path.Combine(path, exe)))
                        return Path.GetFullPath(path);
                }
            }
            throw new FileNotFoundException(new FileNotFoundException().Message, exe);
        }

        private static void OpenLocalFirewall(string appDataPath)
        {
            var lastFirewallConfig = Path.Combine(appDataPath, "lastFirewallConfig");
            var lastExeFile = File.Exists(lastFirewallConfig) ? File.ReadAllText(lastFirewallConfig) : null;
            var curExePath = Path.ChangeExtension(typeof(App).Assembly.Location, "exe");
            if (lastExeFile == curExePath)
                return;

            VhLogger.Current.LogInformation($"Configuring Windows Defender Firewall...");
            var ruleName = "VpnHood";

            //dotnet exe
            var exePath = FindExePath("dotnet.exe");
            Process.Start("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\" dir=in").WaitForExit();
            Process.Start("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=private dir=in").WaitForExit();
            Process.Start("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=private dir=in").WaitForExit();

            // vpnhood exe
            exePath = curExePath;
            if (File.Exists(exePath))
            {
                Process.Start("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=private dir=in").WaitForExit();
                Process.Start("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=private dir=in").WaitForExit();
            }

            // save firewall modified
            File.WriteAllText(lastFirewallConfig, curExePath);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _notifyIcon?.Dispose();
                _appUI?.Dispose();
                _app?.Dispose();
                _fileSystemWatcher?.Dispose();

                // update
                if (_appUpdater.IsUpdated)
                    _appUpdater.LaunchUpdated(new string[] { "/nowindow" });
                _appUpdater?.Dispose();
            }
            _disposed = true;

            // base
            base.Dispose(disposing);
        }
    }
}
