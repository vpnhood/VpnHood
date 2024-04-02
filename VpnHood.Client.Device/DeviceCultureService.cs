
using System.Globalization;

namespace VpnHood.Client.Device;

public class DeviceCultureService : IDeviceCultureService
{
    public string[] SystemCultures => [CultureInfo.InstalledUICulture.TwoLetterISOLanguageName];
    public bool IsSelectedCulturesSupported => false;
    public string[] SelectedCultures { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public string[] AvailableCultures { get; set; } = ["en"];
}