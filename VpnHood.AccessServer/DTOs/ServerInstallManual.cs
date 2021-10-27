namespace VpnHood.AccessServer.DTOs
{
    public class ServerInstallManual
    {
        public ServerInstallAppSettings AppSettings { get; }
        public string LinuxCommand { get; }
        
        public ServerInstallManual(ServerInstallAppSettings appSettings, string linuxCommand)
        {
            AppSettings = appSettings;
            LinuxCommand = linuxCommand;
        }
    }
}