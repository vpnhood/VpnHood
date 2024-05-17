using Microsoft.EntityFrameworkCore;

namespace VpnHood.AccessServer.Repos;

public abstract class RepoBase(DbContext dbContext)
{
    public bool HasChanges()
    {
        return dbContext.ChangeTracker.HasChanges();
    }

    public async ValueTask<T> AddAsync<T>(T model) where T : class
    {
        var entityEntry = await dbContext.AddAsync(model);
        return entityEntry.Entity;
    }

    public Task SaveChangesAsync()
    {
        return dbContext.SaveChangesAsync();
    }
}