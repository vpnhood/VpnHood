namespace VpnHood.AppLib.Abstractions;

public interface IAppCultureProvider
{
    string[] SystemCultures { get; }
    IReadOnlyList<string> AvailableCultures { get; set; }
    string[] SelectedCultures { get; set; }
}