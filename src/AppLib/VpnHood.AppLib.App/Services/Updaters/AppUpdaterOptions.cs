using VpnHood.AppLib.Abstractions.Updaters;

namespace VpnHood.AppLib.App.Services.Updaters;

/// <summary>Provides how the app looks for its updates (<see cref="AppOptions.UpdaterOptions"/>).</summary>
public class AppUpdaterOptions
{
    /// <summary>
    /// Gets the address of the update feed, the JSON of the newest release, or <see langword="null"/> for none.
    /// </summary>
    public Uri? UpdateInfoUrl { get; init; }

    /// <summary>
    /// Gets the updater of the app's channel, such as a store's, or <see langword="null"/> for the feed alone.
    /// </summary>
    public IAppUpdaterProvider? UpdaterProvider { get; init; }

    /// <summary>
    /// Gets how long the app waits after a recorded check of this version before it checks again by itself; a check
    /// that was not recorded, as one the feed did not answer, is tried again at the next chance. The default is a day.
    /// </summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets how long a newer release is left to the updater's own automatic update before the app prompts: from the
    /// date the store gives for the update, or else its first offer the app saw, before it opens the store's screen;
    /// from the release's date before a desktop's notice. The default is three days.
    /// </summary>
    public TimeSpan PromptDelay { get; init; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Gets how long the app prompts no more after the person chose Later, or after the store's screen showed. The
    /// default is seven days.
    /// </summary>
    public TimeSpan PostponePeriod { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets how long the updater's last offer of an update keeps the feed's notice away. The default is seven days.
    /// </summary>
    /// <remarks>
    /// A store out of reach answers that it has no update, as Google Play does with no connection to Google, so only
    /// this long without its offer makes the feed's notice the way. On Google Play that notice leads to a package from
    /// the website, which is the worst case only.
    /// </remarks>
    public TimeSpan UpdaterOfferPeriod { get; init; } = TimeSpan.FromDays(7);
}