
using VpnHood.AppLib.Api.Updaters;

namespace VpnHood.AppLib.App.Services.Updaters;

internal class AppUpdaterData
{
    public Version? PostponeVersion { get; set; }
    public DateTime? PostponeTime { get; set; }

    // the installed version on which the person checked for an update themselves: its notice no longer waits for the
    // automatic update
    public Version? CheckRequestedVersion { get; set; }
    public DateTime? CheckedTime { get; set; }
    public PublishInfo? PublishInfo { get; set; }
    // the installed version the store's update was offered to; an install of any version ends the offer
    public Version? UpdaterAvailableVersion { get; set; }

    // the store's first offer: its own screen waits PromptDelay from it
    public DateTime? UpdaterAvailableSince { get; set; }

    // the store's latest offer: the feed's notice waits UpdaterOfferPeriod from it
    public DateTime? UpdaterLastAvailableTime { get; set; }
    public bool Prompt { get; init; }
}