namespace VpnHood.App.Connect.Ios.Apple;

// The iOS head's entry point. UIKit's Main creates the UIApplication and an AppDelegate as its
// delegate, which starts the Avalonia UI in this process; it then runs the app's loop and never returns.
internal static class App
{
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
