namespace VpnHood.Client.App
{
    public class AppFeatures
    {
        public string Version { get; internal set; } = typeof(VpnHoodApp).Assembly.GetName().Version.ToString(3);
    }
}
