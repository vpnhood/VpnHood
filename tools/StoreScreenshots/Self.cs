using System.Reflection;

namespace VpnHood.App.StoreScreenshots;

// How to start another copy of this tool, whichever way this copy was started: as its own
// executable, or as a library the shared runtime was pointed at (dotnet <dll>).
internal static class Self
{
    private static readonly string Assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location
                                              ?? throw new InvalidOperationException("This tool cannot find its own assembly.");

    public static string FileName { get; } = Environment.ProcessPath
                                             ?? throw new InvalidOperationException("This tool cannot find its own path.");

    public static IReadOnlyList<string> Prefix { get; } =
        Path.GetFileNameWithoutExtension(FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Assembly]
            : [];
}
