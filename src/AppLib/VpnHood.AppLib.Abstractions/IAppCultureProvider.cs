namespace VpnHood.AppLib.Abstractions;

/// <summary>Represents what keeps the app's language: the system's, those the app offers, and the person's choice.</summary>
public interface IAppCultureProvider
{
    /// <summary>Gets the system's languages, by their culture names, in its order.</summary>
    string[] SystemCultures { get; }

    /// <summary>Gets or sets the languages the app offers, by their culture names.</summary>
    IReadOnlyList<string> AvailableCultures { get; set; }

    /// <summary>Gets or sets the languages the person chose for the app, by their culture names; none for the system's.</summary>
    string[] SelectedCultures { get; set; }
}