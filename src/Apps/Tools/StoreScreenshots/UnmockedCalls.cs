using System.Collections.Concurrent;

namespace VpnHood.App.StoreScreenshots;

// The API calls the fixture has no answer for, kept so the run can name them: the web UI engine
// answers an undeclared endpoint with null and lists it at the end, and a page that starts needing
// a new call shows up as a line in the log rather than a silently wrong screenshot. Here the call
// throws as well - a typed contract cannot answer null - and the error dialog the page shows
// fails the shot, with these names beside it as the likely cause.
internal static class UnmockedCalls
{
    private static readonly ConcurrentBag<string> Calls = [];

    public static IReadOnlyList<string> All => Calls.Distinct().Order().ToArray();

    public static NotSupportedException Record(string member)
    {
        Calls.Add(member);
        return new NotSupportedException($"The fixture does not answer {member}.");
    }
}
