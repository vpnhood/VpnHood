using System.CommandLine;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// An access key as a command line gives it: the key itself, or the path of a file holding one. "profile
// add" takes it as its argument; a window's launch as --access-key, which is what opening an access
// key's file runs (the Windows file association, the Store package's) and which the window imports.
internal static class AccessKeyInput
{
    // For that association, not for a person, so help leaves it out.
    public static Option<string> CreateOption()
    {
        return new Option<string>("--access-key") {
            Description = "An access key (vh://...), or the path of a file holding one, to add and choose.",
            Hidden = true
        };
    }

    // A key on the command line is in that person's shell history and in ps; a file is how a
    // script should pass one, so both are taken and the file wins when the argument names one.
    public static string Read(string keyOrPath)
    {
        return File.Exists(keyOrPath) ? File.ReadAllText(keyOrPath).Trim() : keyOrPath.Trim();
    }
}
