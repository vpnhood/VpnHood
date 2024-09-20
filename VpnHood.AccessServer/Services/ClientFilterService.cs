using System.Data;
using System.Text.RegularExpressions;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.ClientFilters;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class ClientFilterService(VhRepo vhRepo)
{
    public async Task<ClientFilter> Create(Guid projectId, ClientFilterCreate createParams)
    {
        ValidateFilter(createParams.Filter);

        var clientFilter = new ClientFilterModel {
            ClientFilterId = 0,
            ProjectId = projectId,
            ClientFilterName = createParams.ClientFilterName,
            Filter = createParams.Filter
        };

        await vhRepo.AddAsync(clientFilter);
        await vhRepo.SaveChangesAsync();

        return clientFilter.ToDto();
    }

    public async Task<ClientFilter> Get(Guid projectId, int clientFilterId)
    {
        var clientFilter = await vhRepo.ClientFilterGet(projectId, clientFilterId);
        return clientFilter.ToDto();
    }

    public async Task<ClientFilter[]> List(Guid projectId)
    {
        var clientFilters = await vhRepo.ClientFilterList(projectId);
        return clientFilters
            .Select(x => x.ToDto())
            .ToArray();
    }

    public async Task Update(Guid projectId, int clientFilterId, ClientFilterUpdate updateParams)
    {
        var clientFilter = await vhRepo.ClientFilterGet(projectId, clientFilterId);
        
        if (updateParams.ClientFilterName != null) clientFilter.ClientFilterName = updateParams.ClientFilterName.Value.Trim();
        if (updateParams.Filter != null) {
            ValidateFilter(updateParams.Filter.Value);
            clientFilter.Filter = updateParams.Filter.Value.Trim();
        }

        await vhRepo.SaveChangesAsync();
    }

    private void ValidateFilter(string filter)
    {
        ValidateFilter(filter, []);
    }

    private void ValidateFilter(string filter, string[] tags)
    {
        // replace shorthand operators with full operators
        filter = filter
            .Replace("&&", "AND")
            .Replace("&", "AND")
            .Replace("||", "OR")
            .Replace("|", "OR")
            .Replace("!", "NOT ");

        // Regex to match "tag_xxx" pattern
        filter = Regex.Replace(filter, @"#[\w:]+", match =>
            tags.Contains(match.Value, StringComparer.OrdinalIgnoreCase).ToString());

        var dataTable = new DataTable();
        dataTable.Compute(filter, string.Empty);
    }
}