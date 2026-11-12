namespace VpnHood.AppLib.Api.App;

/// <summary>Represents the person's rating of the app.</summary>
public class AppUserReview
{
    /// <summary>Gets or sets the rating, as the app's rating dialog gives it.</summary>
    public required int Rating { get; set; }

    /// <summary>Gets or sets the person's text, which may be empty.</summary>
    public required string ReviewText { get; set; }
}