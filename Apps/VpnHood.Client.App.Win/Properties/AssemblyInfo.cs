using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]



namespace VpnHood.Client.App.Win.Properties;

public static class AssemblyInfo
{
    public static bool IsDebugMode {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}