using VpnHood.Client.App.UI;
using System;
using System.Diagnostics;
using System.Windows.Forms;
using VpnHood.Common;
using VpnHood.Logging;
using System.Threading;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;

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
            VhLogger.Current = VhLogger.CreateConsoleLogger();
            VhLogger.Current.LogInformation($"{typeof(App).Assembly.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            var appDataPath = new AppOptions().AppDataPath; // we use defaultPath
            var lastAppUrl = Path.Combine(appDataPath, "LastAppUrl");

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(0), false))
            {
                // open main window if app is already running and user run the app again
                if (File.Exists(lastAppUrl))
                {
                    var runningAppUrl = File.ReadAllText(lastAppUrl);
                    OpenMainWindow(runningAppUrl);
                }
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

            // configuring Windows Frewall
            try
            {
                OpenLocalFirewall(appDataPath);
            }
            catch { };

            // init app
            _app = VpnHoodApp.Init(new WinAppProvider(), new AppOptions() { LogToConsole = logToConsole });
            _appUI = VpnHoodAppUI.Init(new MemoryStream(Resource.SPA));
            File.WriteAllText(lastAppUrl, _appUI.Url);

            // create notification icon
            InitNotifyIcon();

            // MainWindow
            if (openWindow)
                OpenMainWindow();

            // Message Loop
            Application.Run();
        }

        public void OpenMainWindow(string url = null)
        {
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
            menu.Items.Add(AppUIResource.Exit, null, (sender, e) => Exit());
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
