namespace VpnHood.AppUi.Hosting.Desktop.Abstractions;

/// <summary>Defines the app's API address, where its web host serves the UI's page too; a rebind may move it.</summary>
public interface IDesktopApiUrlProvider
{
    /// <summary>Gets the latest known address; while nothing answers, it is the last one, which a load fails against and tries again.</summary>
    Uri Current { get; }

    /// <summary>
    /// Occurs when the address may have moved, as when the service answers again, on any thread: a UI that loaded its page
    /// from <see cref="Current"/> reads it again and reloads.
    /// </summary>
    event EventHandler? Changed;
}
