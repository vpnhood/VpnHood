using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<IDbContextTransaction?> WithNoLockTransaction()
    {
        return dbContext.Database.CurrentTransaction == null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted)
            : null;
    }


    public Task SaveChangesAsync()
    {
        return dbContext.SaveChangesAsync();
    }
}