namespace VpnHood.Client.Device;

public interface ICultureService
{
    string[] SystemCultures { get; }
    string[] AvailableCultures { get; set; }
    string[] SelectedCultures { get; set; }
}