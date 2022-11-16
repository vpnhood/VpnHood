#nullable disable
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Agent.Persistence;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhContext : VhContextBase
{

    public VhContext(DbContextOptions<VhContext> options)
        : base(options)
    {
    }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}