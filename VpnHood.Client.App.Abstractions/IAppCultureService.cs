namespace VpnHood.Client.App.Abstractions;

public interface IAppCultureService
{
    string[] SystemCultures { get; }
    string[] AvailableCultures { get; set; }
    string[] SelectedCultures { get; set; }
}