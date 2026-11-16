namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Represents an ad the app showed for a session.
/// </summary>
public class AdResult
{
    /// <summary>Gets the data that proves the ad to the server, as a rewarded ad's, or <see langword="null"/>.</summary>
    public required string? AdData { get; init; }

    /// <summary>Gets a value indicating whether the user clicked the ad.</summary>
    public required bool IsClicked { get; init; }

    /// <summary>Gets the name of the ad network that served the ad.</summary>
    public required string NetworkName { get; init; }
}