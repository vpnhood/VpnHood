using VpnHood.App.Connect.Ios;
using VpnHood.AppLib;
using VpnHood.AppLib.Utils;

// The UI framework is chosen here, before UIKit is told its delegate: UIKit's own delegate hosts
// the web UI, Avalonia's the native UI when the debug command forces it. The app is not up yet -
// the delegate starts it - so the setting is read from its file.
var storageFolderPath = AppOptions.BuildStorageFolderPath(AppConfigs.AppName);
var delegateType = VpnHoodApp.HasDebugCommand(storageFolderPath, DebugCommands.AvaloniaUi)
    ? typeof(AvaloniaUiAppDelegate)
    : typeof(AppDelegate);
UIApplication.Main(args, null, delegateType);
