using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AuthorizationTest
    {
        [TestMethod]
        public async Task Seeding()
        {
            await using VhContext vhContext = new();

            // Create new base types
            var newSecureObjectType1 = new SecureObjectType(Guid.NewGuid(), Guid.NewGuid().ToString());
            var secureObjectTypes = SecureObjectTypes.All.Concat(new[] { newSecureObjectType1 }).ToArray();

            var maxPermissionId = vhContext.Permissions.Max(x => (int?)x.PermissionId) ?? 100;
            var newPermission = new Permission(maxPermissionId + 1, Guid.NewGuid().ToString());
            var permissions = Permissions.All.Concat(new[] { newPermission }).ToArray();

            var newPermissionGroup1 = new PermissionGroup(Guid.NewGuid(), Guid.NewGuid().ToString())
            {
                Permissions = new List<Permission> { newPermission }
            };
            var permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup1 }).ToArray();
            await vhContext.Init(secureObjectTypes, permissions, permissionGroups);

            await using (VhContext vhContext2 = new())
            {

                //-----------
                // check: new type is inserted
                //-----------
                Assert.AreEqual(newSecureObjectType1.SecureObjectTypeName,
                    vhContext2.SecureObjectTypes.Single(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId).SecureObjectTypeName);

                //-----------
                // check: new permission is inserted
                //-----------
                Assert.AreEqual(newPermission.PermissionName,
                    vhContext2.Permissions.Single(x => x.PermissionId == newPermission.PermissionId).PermissionName);

                //-----------
                // check new permission group is inserted
                //-----------
                Assert.AreEqual(newPermissionGroup1.PermissionGroupName,
                    vhContext2.PermissionGroups
                        .Single(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId).PermissionGroupName);

                //-----------
                // check: new permission group permissions in inserted
                //-----------
                Assert.IsTrue(vhContext2.PermissionGroups.Include(x => x.Permissions)
                    .Single(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId)
                    .Permissions.Any(x => x.PermissionId == newPermission.PermissionId));

                //-----------
                // check: System object is not deleted
                //-----------
                Assert.IsTrue(vhContext2.SecureObjectTypes.Any(x => x.SecureObjectTypeId == AuthManager.SystemSecureObjectTypeId));
                Assert.IsTrue(vhContext2.PermissionGroups.Any(x =>
                    x.PermissionGroupId == AuthManager.SystemPermissionGroupId));
            }

            //-----------
            // check: update SecureObjectTypeName
            //-----------
            newSecureObjectType1.SecureObjectTypeName = "new-name_" + Guid.NewGuid();
            await vhContext.Init(secureObjectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
                Assert.AreEqual(newSecureObjectType1.SecureObjectTypeName, vhContext2.SecureObjectTypes.Single(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId).SecureObjectTypeName);

            //-----------
            // check: add/remove SecureObjectTypeName
            //-----------
            SecureObjectType newSecureObjectType2 = new(Guid.NewGuid(), Guid.NewGuid().ToString());
            secureObjectTypes = SecureObjectTypes.All.Concat(new[] { newSecureObjectType2 }).ToArray();
            await vhContext.Init(secureObjectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
            {
                Assert.IsTrue(vhContext2.SecureObjectTypes.Any(x => x.SecureObjectTypeId == newSecureObjectType2.SecureObjectTypeId));
                Assert.IsFalse(vhContext2.SecureObjectTypes.Any(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId));
            }

            //-----------
            // check: add/remove new PermissionGroup
            //-----------
            PermissionGroup newPermissionGroup2 = new(Guid.NewGuid(), Guid.NewGuid().ToString())
            {
                Permissions = new List<Permission> { newPermission }
            };
            permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup2 }).ToArray();
            await vhContext.Init(secureObjectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
            {
                Assert.IsTrue(vhContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup2.PermissionGroupId));
                Assert.IsFalse(vhContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId));
            }

        }

        [TestMethod]
        public void Foo()
        {
            using VhContext vhContext = new();
            var query = from b in vhContext.SecureObjectHierarchy(AuthManager.SystemSecureObjectId)
                        select b;
            var z = query.ToArray();

        }

        [TestMethod]
        public async Task Rename_permission_group()
        {
            await using VhContext vhContext = new();

            var secureObject = await vhContext.AuthManager.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project);
            await vhContext.SaveChangesAsync();

            //-----------
            // check: assigned permission group should remain intact after renaming its name
            //-----------
            var guest1 = Guid.NewGuid();

            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObject.SecureObjectId, guest1, Permissions.ListTokens));
            await vhContext.AuthManager.SecureObject_AddUserPermission(secureObject, guest1,
                PermissionGroups.ProjectViewer, AuthManager.SystemUserId);
            PermissionGroups.ProjectViewer.PermissionGroupName = Guid.NewGuid().ToString();
            await vhContext.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObject.SecureObjectId, guest1, Permissions.ListTokens));

            //-----------
            // check: used SecureObjectType should not be deleted
            //-----------
            try
            {
                vhContext.SecureObjectTypes.Remove(SecureObjectTypes.Project);
                await vhContext.SaveChangesAsync();
                Assert.Fail("No cascade expected for SecureObjectType!");
            }
            catch { /* ignored */ }
        }

        [TestMethod]
        public async Task InheritanceAccess()
        {
            await using VhContext vhContext = new();

            var secureObjectL1 = await vhContext.AuthManager.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project);
            var secureObjectL2 = await vhContext.AuthManager.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL1);
            var secureObjectL3 = await vhContext.AuthManager.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL2);
            var secureObjectL4 = await vhContext.AuthManager.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL3);

            // add guest1 to Role1
            var guest1 = Guid.NewGuid();
            var role1 = await vhContext.AuthManager.Role_Create(Guid.NewGuid().ToString(), AuthManager.SystemUserId);
            await vhContext.AuthManager.Role_AddUser(role1.RoleId, guest1, AuthManager.SystemUserId);

            // add guest2 to Role2
            var guest2 = Guid.NewGuid();
            var role2 = await vhContext.AuthManager.Role_Create(Guid.NewGuid().ToString(), AuthManager.SystemUserId);
            await vhContext.AuthManager.Role_AddUser(role2.RoleId, guest2, AuthManager.SystemUserId);

            //-----------
            // check: inheritance: add role1 to L3 and it shouldn't access to L1
            //-----------
            await vhContext.SaveChangesAsync();
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest1, Permissions.ListTokens));

            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest2, Permissions.ListTokens));


            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObjectL3, role1, PermissionGroups.ProjectViewer, AuthManager.SystemUserId);
            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObjectL1, role2, PermissionGroups.ProjectViewer, AuthManager.SystemUserId);
            await vhContext.SaveChangesAsync();
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsFalse(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest1, Permissions.ListTokens));
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest1, Permissions.ListTokens));

            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest2, Permissions.ListTokens));
            Assert.IsTrue(await vhContext.AuthManager.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest2, Permissions.ListTokens));
        }

    }
}