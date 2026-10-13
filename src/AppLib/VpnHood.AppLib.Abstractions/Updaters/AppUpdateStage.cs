namespace VpnHood.AppLib.Abstractions.Updaters;

public enum AppUpdateStage
{
    None,
    Available,
    Downloading,
    Downloaded,
    Installing,
    Failed
}
