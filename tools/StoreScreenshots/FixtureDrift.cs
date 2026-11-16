using System.Collections.Concurrent;

namespace VpnHood.App.StoreScreenshots;

// What the recording no longer says, gathered from every picture and said once.
//
// A member the app's API now requires and the fixture predates is filled with a plain build's
// default, and every picture reports the same list - once per shot it would bury the run's own
// lines. The fix is never here: the fixture is re-recorded from a current client.
internal static class FixtureDrift
{
    private static readonly ConcurrentDictionary<string, byte> Notes = new();

    public static void Note(string line)
    {
        Notes.TryAdd(line.Trim(), 0);
    }

    public static void Report(string fixturePath)
    {
        if (Notes.IsEmpty)
            return;

        Console.WriteLine($"\n{Path.GetFileName(fixturePath)} is behind the app's API - re-record it from a current client:");
        foreach (var note in Notes.Keys.Order())
            Console.WriteLine($"  {note}");
    }
}
