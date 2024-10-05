using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.ClientFilters;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Repos;
using VpnHood.Manager.Common.Utils;

namespace VpnHood.AccessServer.Services;

public class ClientFilterService(
    VhRepo vhRepo,
    AgentCacheClient agentCacheClient)
{
    public async Task<ClientFilter> Create(Guid projectId, ClientFilterCreateParams createParamsParams)
    {
        ValidateFilter(createParamsParams.Filter);

        var clientFilter = new ClientFilterModel {
            ClientFilterId = 0,
            ProjectId = projectId,
            ClientFilterName = createParamsParams.ClientFilterName,
            Description = createParamsParams.Description,
            Filter = createParamsParams.Filter
        };

        await vhRepo.AddAsync(clientFilter);
        await vhRepo.SaveChangesAsync();
        await agentCacheClient.InvalidateProject(projectId);

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

    public async Task<ClientFilter> Update(Guid projectId, int clientFilterId, ClientFilterUpdateParams updateParamsParams)
    {
        var clientFilter = await vhRepo.ClientFilterGet(projectId, clientFilterId);
        
        if (updateParamsParams.ClientFilterName != null) clientFilter.ClientFilterName = updateParamsParams.ClientFilterName.Value.Trim();
        if (updateParamsParams.Description != null) clientFilter.Description = updateParamsParams.Description.Value;
        if (updateParamsParams.Filter != null && !updateParamsParams.Filter.Value.Equals(clientFilter.Filter)) {
            ValidateFilter(updateParamsParams.Filter.Value);
            clientFilter.Filter = updateParamsParams.Filter.Value.Trim();
            await agentCacheClient.InvalidateProject(projectId);
        }

        await vhRepo.SaveChangesAsync();
        return clientFilter.ToDto();
    }

    public async Task Delete(Guid projectId, int clientFilterId)
    {
        await vhRepo.ClientFilterDelete(projectId, clientFilterId);
        await vhRepo.SaveChangesAsync();
    }

    private static void ValidateFilter(string filter)
    {
        ClientFilterUtils.Validate(filter, []);
    }
}