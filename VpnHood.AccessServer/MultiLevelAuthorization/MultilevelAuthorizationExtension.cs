using System.Threading.Tasks;
using GrayMint.Common.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using VpnHood.AccessServer.MultiLevelAuthorization.Persistence;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;

namespace VpnHood.AccessServer.MultiLevelAuthorization;

public static class MultilevelAuthorizationExtension
{
    public static IServiceCollection AddMultilevelAuthorization(this IServiceCollection services)
    {
        services.AddScoped<MultilevelAuthRepo>();
        return services;
    }

    public static async Task<IApplicationBuilder> UseMultilevelAuthorization(this IApplicationBuilder app)
    {
        await using var scope = app.ApplicationServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MultilevelAuthContext>();
        await EfCoreUtil.EnsureTablesCreated(dbContext);
        await dbContext.SaveChangesAsync();
        return app;
    }
}