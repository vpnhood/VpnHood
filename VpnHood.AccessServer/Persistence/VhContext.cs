using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

#nullable disable
namespace VpnHood.AccessServer.Persistence;

// ReSharper disable once PartialTypeWithSinglePart
public partial class VhContext : VhContextBase
{
    public virtual DbSet<User> Users { get; set; }

    public VhContext(DbContextOptions<VhContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.UserId);

            entity.Property(e => e.AuthUserId)
                .HasMaxLength(255);

            entity.Property(e => e.AuthCode)
                .HasMaxLength(50);

            entity.Property(e => e.UserName)
                .HasMaxLength(100);

            entity.Property(e => e.Email)
                .HasMaxLength(100);

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.HasIndex(e => e.UserName)
                .IsUnique();
        });

        // ReSharper disable once InvocationIsSkipped
        OnModelCreatingPartial(modelBuilder);
    }

    // ReSharper disable once PartialMethodWithSinglePart
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}