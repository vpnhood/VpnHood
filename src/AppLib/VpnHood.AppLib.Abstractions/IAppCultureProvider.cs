namespace VpnHood.AppLib.Abstractions;

public interface IAppCultureProvider
{
    string[] SystemCultures { get; }
    string[] AvailableCultures { get; set; }
    string[] SelectedCultures { get; set; }
}