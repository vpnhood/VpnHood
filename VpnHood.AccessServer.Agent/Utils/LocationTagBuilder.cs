using System.Linq;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.Manager.Common;
using VpnHood.Manager.Common.Utils;

namespace VpnHood.AccessServer.Agent.Utils;

public static class LocationTagBuilder
{
    private static IEnumerable<string> GetPlanTags(ServerCache server, ProjectCache project, string[] clientTags)
    {
        clientTags = clientTags.Except(BuiltInTags.PlanTags).ToArray();
        foreach (var planTag in BuiltInTags.PlanTags) {

            // check is tag not required for the client
            if (LoadBalancerService.IsMatchClientFilter(project, server, clientTags))
                continue;

            // check is tag required for the client
            if (LoadBalancerService.IsMatchClientFilter(project, server, clientTags.Concat([planTag])))
                yield return planTag;
        }
    }

    public static string BuildServerLocationWithTags(string location, IEnumerable<ServerCache> servers, ProjectCache project, string[] clientTags)
    {
        // find all servers in the location
        var items = servers
            .Where(server => server.LocationInfo.ServerLocation == location)
            .Select(server => new { Server = server, Tags = server.Tags.Concat(GetPlanTags(server, project, clientTags)) })
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