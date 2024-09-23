using VpnHood.AccessServer.Dtos.ClientFilters;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ClientFilterConverter
{
    public static ClientFilter ToDto(this ClientFilterModel clientFilterModel)
    {
        return new ClientFilter {
            ClientFilterId = clientFilterModel.ClientFilterId.ToString(),
            ClientFilterName = clientFilterModel.ClientFilterName,
            Filter = clientFilterModel.Filter,
            Description = clientFilterModel.Description
        };
    }
}
