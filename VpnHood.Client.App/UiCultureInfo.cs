using System.Globalization;

namespace VpnHood.Client.App;

public class UiCultureInfo(CultureInfo cultureInfo)
{
    public UiCultureInfo(string code)
        : this(new CultureInfo(code))
    {
    }

    public string Code { get; } = cultureInfo.Name;
    public string NativeName { get; } = cultureInfo.NativeName;
    public UiCultureInfo? ParentCode { get; } = string.IsNullOrEmpty(cultureInfo.Parent.Name) 
        ? null : new UiCultureInfo(cultureInfo.Parent);

}