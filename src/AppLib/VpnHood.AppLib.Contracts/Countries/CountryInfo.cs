namespace VpnHood.AppLib.Contracts.Countries;

public class CountryInfo
{
    public required string EnglishName { get; init; }
    public required string TranslatedName { get; init; }
    public required string CountryCode { get; init; }
}