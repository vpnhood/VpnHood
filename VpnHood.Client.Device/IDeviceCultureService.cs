namespace VpnHood.Client.Device;

public interface IDeviceCultureService
{
    string[] SystemCultures { get; }
    bool IsSelectedCulturesSupported { get; }
    string[] SelectedCultures { get; set; }
    string[] AvailableCultures { get; set; }
}