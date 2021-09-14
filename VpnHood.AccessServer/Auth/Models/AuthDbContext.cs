using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace VpnHood.AccessServer.Auth.Models
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
        }

        protected AuthDbContext(DbContextOptions options)
            : base(options)
        {
        }

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

                entity.HasIndex(e=>e.PermissionName)
                    .IsUnique();
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
                entity.HasIndex(e => new {e.ObjectId, e.ObjectTypeId})
                    .IsUnique();
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

        public void Foo()
        {
        }

        private static Dictionary<string, int> EnumToDictionary<T>()
        {
            var names = Enum.GetNames(typeof(T));
            var values = (int[])Enum.GetValues(typeof(T));
            var items = new Dictionary<string, int>();
            for (var i = 0; i < names.Length; i++)
                items.Add(names[i], values[i]);
            return items;
        }

        private async Task InitPermissions<T>() where T : Enum
        {
            // convert TPermission
            var curItems = EnumToDictionary<T>();
            var dbValues = await Permissions.ToArrayAsync();
            var toDelete = dbValues.Where(x => !curItems.Any(c => c.Value == x.PermissionId && c.Key == x.PermissionName));
            var toAddPermissions = curItems.Where(x => !dbValues.Any(c => x.Value == c.PermissionId && x.Key == c.PermissionName));
            Permissions.RemoveRange(toDelete);
            Permissions.AddRange(toAddPermissions.Select(x => new Permission { PermissionId = x.Value, PermissionName = x.Key }));
        }

        private async Task InitObjectTypes<T>() where T : Enum
        {
            // convert TPermission
            var curItems = EnumToDictionary<T>();
            var dbValues = await ObjectTypes.ToArrayAsync();
            var toDelete = dbValues.Where(x => !curItems.Any(c => c.Value == x.ObjectTypeId && c.Key == x.ObjectTypeName));
            var toAddPermissions = curItems.Where(x => !dbValues.Any(c => x.Value == c.ObjectTypeId && x.Key == c.ObjectTypeName));
            ObjectTypes.RemoveRange(toDelete);
            ObjectTypes.AddRange(toAddPermissions.Select(x => new ObjectType { ObjectTypeId = x.Value, ObjectTypeName = x.Key }));
        }

        public async Task Init<TObjectType, TPermission>() 
            where TObjectType : Enum 
            where TPermission : Enum 
        {
            await InitObjectTypes<TObjectType>();
            await InitPermissions<TPermission>();

            await SaveChangesAsync();
        }

    }
}