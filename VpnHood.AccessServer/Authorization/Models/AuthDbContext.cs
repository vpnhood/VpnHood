using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace VpnHood.AccessServer.Authorization.Models
{
    public abstract class AuthDbContext : DbContext
    {
        public const string Schema = "auth";

        public virtual DbSet<SecureObjectType> SecureObjectTypes { get; set; }
        public virtual DbSet<PermissionGroup> PermissionGroups { get; set; }
        public virtual DbSet<Permission> Permissions { get; set; }
        public virtual DbSet<PermissionGroupPermission> PermissionGroupPermissions { get; set; }
        public virtual DbSet<Role> Roles { get; set; }
        public virtual DbSet<RoleUser> RoleUsers { get; set; }
        public virtual DbSet<SecureObject> SecureObjects { get; set; }
        public virtual DbSet<SecureObjectRolePermission> SecureObjectRolePermissions { get; set; }
        public virtual DbSet<SecureObjectUserPermission> SecureObjectUserPermissions { get; set; }
        public IQueryable<SecureObject> SecureObjectHierarchy(Guid id)
            => FromExpression(() => SecureObjectHierarchy(id));

        protected AuthDbContext()
        {
            AuthManager = new AuthManager(this);
        }

        protected AuthDbContext(DbContextOptions options)
            : base(options)
        {
            AuthManager = new AuthManager(this);
        }

        public AuthManager AuthManager { get; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SecureObjectType>(entity =>
            {
                entity.ToTable(nameof(SecureObjectTypes), Schema);
                entity.Property(e => e.SecureObjectTypeId)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.SecureObjectTypeName)
                    .IsUnique();
            });

            modelBuilder.Entity<Permission>(entity =>
            {
                entity.ToTable(nameof(Permissions), Schema);

                entity.Property(e => e.PermissionId)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.PermissionName)
                    .IsUnique();

                entity
                    .HasMany(p => p.PermissionGroups)
                    .WithMany(p => p.Permissions)
                    .UsingEntity<PermissionGroupPermission>(
                        j => j
                            .HasOne(pt => pt.PermissionGroup)
                            .WithMany(t => t.PermissionGroupPermissions)
                            .HasForeignKey(pt => pt.PermissionGroupId),
                        j => j
                            .HasOne(pt => pt.Permission)
                            .WithMany(p => p.PermissionGroupPermissions)
                            .HasForeignKey(pt => pt.PermissionId)
                    );
            });

            modelBuilder.Entity<PermissionGroup>(entity =>
            {
                entity.ToTable(nameof(PermissionGroups), Schema);

                entity.Property(e => e.PermissionGroupId)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.PermissionGroupName)
                    .IsUnique();
            });

            modelBuilder.Entity<PermissionGroupPermission>(entity =>
            {
                entity.ToTable(nameof(PermissionGroupPermissions), Schema);
                entity.HasKey(e => new { e.PermissionGroupId, e.PermissionId });
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.ToTable(nameof(Roles), Schema);
            });

            modelBuilder.Entity<RoleUser>(entity =>
            {
                entity.ToTable(nameof(RoleUsers), Schema);

                entity.HasKey(e => new { e.UserId, e.RoleId });
            });

            modelBuilder.Entity<SecureObject>(entity =>
            {
                entity.ToTable(nameof(SecureObjects), Schema);

                entity.HasOne(e => e.SecureObjectType)
                    .WithMany(d => d.SecureObjects)
                    .OnDelete(DeleteBehavior.NoAction); //NoAction, dangerous actions
            });

            modelBuilder.Entity<SecureObjectRolePermission>(entity =>
            {
                entity.ToTable(nameof(SecureObjectRolePermissions), Schema);

                entity.HasKey(e => new { e.SecureObjectId, e.RoleId, e.PermissionGroupId });
            });

            modelBuilder.Entity<SecureObjectUserPermission>(entity =>
            {
                entity.ToTable(nameof(SecureObjectUserPermissions), Schema);

                entity.HasKey(e => new { e.SecureObjectId, UsedId = e.UserId, e.PermissionGroupId });
            });

            // functions
            modelBuilder
                .HasDbFunction(typeof(AuthDbContext).GetMethod(nameof(SecureObjectHierarchy), new[] {typeof(Guid)})!)
                .HasSchema(Schema)
                .HasName(nameof(SecureObjectHierarchy));
        }

        public async Task Init(SecureObjectType[] secureObjectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
        {
            await AuthManager.Init(secureObjectTypes, permissions, permissionGroups, removeOtherPermissionGroups);
        }

    }
}