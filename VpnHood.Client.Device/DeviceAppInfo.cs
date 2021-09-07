namespace VpnHood.Client.Device
{
    public class DeviceAppInfo
    {
        public DeviceAppInfo(string appId, string appName, string iconPng)
        {
            AppId = appId;
            AppName = appName;
            IconPng = iconPng;
        }

        public string AppId { get; set; }
        public string AppName { get; set; }
        public string IconPng { get; set; }
    }
}