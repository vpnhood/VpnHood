namespace VpnHood.Client.Device;

public interface IDeviceCultureService
{
    bool IsSelectedCulturesSupported { get; }
    bool IsAvailableCultureSupported { get; }
    string[] SelectedCultures { get; set; }
    string[] AvailableCultures { get; set; }
}