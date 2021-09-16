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
        private const string Schema = "auth";

        public virtual DbSet<ObjectType> ObjectTypes { get; set; }
        public virtual DbSet<PermissionGroup> PermissionGroups { get; set; }
        public virtual DbSet<Permission> Permissions { get; set; }
        public virtual DbSet<PermissionGroupPermission> PermissionGroupPermissions { get; set; }
        public virtual DbSet<Role> Roles { get; set; }
        public virtual DbSet<RoleUser> RoleUsers { get; set; }
        public virtual DbSet<SecurityDescriptor> SecurityDescriptors { get; set; }
        public virtual DbSet<SecurityDescriptorRolePermission> SecurityDescriptorRolePermissions { get; set; }
        public virtual DbSet<SecurityDescriptorUserPermission> SecurityDescriptorUserPermissions { get; set; }

        protected AuthDbContext()
        {
            Manager = new AuthManager(this);
        }

        protected AuthDbContext(DbContextOptions options)
            : base(options)
        {
            Manager = new AuthManager(this);
        }

        public AuthManager Manager { get; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ObjectType>(entity =>
            {
                entity.ToTable(nameof(ObjectTypes), Schema);
                entity.Property(e => e.ObjectTypeId)
                    .ValueGeneratedNever();

                entity.HasIndex(e => e.ObjectTypeName)
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

            modelBuilder.Entity<SecurityDescriptor>(entity =>
            {
                entity.ToTable(nameof(SecurityDescriptors), Schema);
                entity.HasIndex(e => e.ObjectId)
                    .IsUnique();

                entity.HasOne(e => e.ObjectType)
                    .WithMany(d => d.SecurityDescriptors)
                    .OnDelete(DeleteBehavior.NoAction); //NoAction, dangerous actions
            });

            modelBuilder.Entity<SecurityDescriptorRolePermission>(entity =>
            {
                entity.ToTable(nameof(SecurityDescriptorRolePermissions), Schema);

                entity.HasKey(e => new { e.SecurityDescriptorId, e.RoleId, e.PermissionGroupId });
            });

            modelBuilder.Entity<SecurityDescriptorUserPermission>(entity =>
            {
                entity.ToTable(nameof(SecurityDescriptorUserPermissions), Schema);

                entity.HasKey(e => new { e.SecurityDescriptorId, e.UsedId, e.PermissionGroupId });
            });
        }

        public async Task Init(ObjectType[] objectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
        {
            await Manager.Init(objectTypes, permissions, permissionGroups, removeOtherPermissionGroups);
        }

    }
}