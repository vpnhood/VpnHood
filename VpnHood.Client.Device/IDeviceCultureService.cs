namespace VpnHood.Client.Device;

public interface IDeviceCultureService
{
    bool IsSelectedCulturesSupported { get; }
    bool IsAppCulturesSupported { get; }
    string[] SystemCultures { get; }
    string[]? SelectedCultures { get; set; }
    string[]? AppCultures { get; set; }
}