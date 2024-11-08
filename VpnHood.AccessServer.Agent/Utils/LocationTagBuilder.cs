using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.Manager.Common.Utils;

namespace VpnHood.AccessServer.Agent.Utils;

public static class LocationTagBuilder
{
    public static string BuildServerLocationWithTags(string location, IEnumerable<ServerCache> servers, ProjectCache project)
    {
        // find all servers in the location
        var items = servers
            .Where(server => server.LocationInfo.ServerLocation == location)
            .Select(server => new {
                Server = server,
                Tags = server.Tags
            })
            .ToArray();

        // add all tags from servers in the location
        var tags = items
            .SelectMany(item => item.Tags)
            .Distinct();

        // add partial sign (~) to the location if the tags does not exist on all servers in this location
        var newTags = tags
            .Select(tag => items.All(item => item.Tags.Contains(tag)) ? tag : $"~{tag}")
            .Order()
            .ToArray();

        return newTags.Length > 0
            ? $"{location} [{TagUtils.TagsToString(newTags, false)}]"
            : location;
    }
}