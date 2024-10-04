using VpnHood.AccessServer.Dtos.FarmTokenRepos;
using VpnHood.AccessServer.Dtos.ServerFarms;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerFarmConverter
{
    public static ServerFarm ToDto(this ServerFarmModel model, string serverProfileName)
    {
        var dto = new ServerFarm {
            ServerFarmName = model.ServerFarmName,
            ServerFarmId = model.ServerFarmId,
            UseHostName = model.UseHostName,
            CreatedTime = model.CreatedTime,
            ServerProfileId = model.ServerProfileId,
            ServerProfileName = serverProfileName,
            Secret = model.Secret,
            TokenError = model.TokenError,
            PushTokenToClient = model.PushTokenToClient,
            MaxCertificateCount = model.MaxCertificateCount,
            Certificate = model.Certificates != null ? model.GetCertificateInToken().ToDto() : null
        };

        return dto;
    }

    public static FarmTokenRepo ToDto(this FarmTokenRepoModel model)
    {
        return new FarmTokenRepo {
            FarmTokenRepoId = model.FarmTokenRepoId.ToString(),
            FarmTokenRepoName = model.FarmTokenRepoName,
            PublishUrl = model.PublishUrl,
            RepoSettings = model.GetRepoSettings(),
            IsUpToDate = null,
            Error = model.Error
        };
    }
}