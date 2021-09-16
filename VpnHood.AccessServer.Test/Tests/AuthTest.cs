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
    public class AuthTest
    {
        [TestMethod]
        public async Task Seed_Update()
        {
            await using VhContext vhContext = new();

            // Create new base types
            ObjectType newObjectType1 = new(Guid.NewGuid(), Guid.NewGuid().ToString());
            ObjectType[] objectTypes = { newObjectType1 };

            Permission newPermission = new(vhContext.Permissions.Max(x => x.PermissionId) + 1, Guid.NewGuid().ToString());
            Permission[] permissions = Permissions.All.Concat(new[] { newPermission }).ToArray();

            PermissionGroup newPermissionGroup1 = new(Guid.NewGuid(), Guid.NewGuid().ToString())
            {
                Permissions = new List<Permission> { newPermission }
            };
            PermissionGroup[] permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup1 }).ToArray();
            await vhContext.Init(objectTypes, permissions, permissionGroups);

            await using (VhContext vhContext2 = new())
            {

                //-----------
                // check: new type is inserted
                //-----------
                Assert.AreEqual(newObjectType1.ObjectTypeName,
                    vhContext2.ObjectTypes.Single(x => x.ObjectTypeId == newObjectType1.ObjectTypeId).ObjectTypeName);

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
                Assert.IsTrue(vhContext2.ObjectTypes.Any(x => x.ObjectTypeId == AuthManager.SystemObjectTypeId));
                Assert.IsTrue(vhContext2.PermissionGroups.Any(x =>
                    x.PermissionGroupId == AuthManager.SystemPermissionGroupId));
            }

            //-----------
            // check: update ObjectTypeName
            //-----------
            newObjectType1.ObjectTypeName = "new-name_" + Guid.NewGuid();
            await vhContext.Init(objectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
                Assert.AreEqual(newObjectType1.ObjectTypeName, vhContext2.ObjectTypes.Single(x => x.ObjectTypeId == newObjectType1.ObjectTypeId).ObjectTypeName);

            //-----------
            // check: add/remove ObjectTypeName
            //-----------
            ObjectType newObjectType2 = new(Guid.NewGuid(), Guid.NewGuid().ToString());
            objectTypes = new[] { newObjectType2 };
            await vhContext.Init(objectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
            {
                Assert.IsTrue(vhContext2.ObjectTypes.Any(x => x.ObjectTypeId == newObjectType2.ObjectTypeId));
                Assert.IsFalse(vhContext2.ObjectTypes.Any(x => x.ObjectTypeId == newObjectType1.ObjectTypeId));
            }

            //-----------
            // check: add/remove new PermissionGroup
            //-----------
            PermissionGroup newPermissionGroup2 = new(Guid.NewGuid(), Guid.NewGuid().ToString())
            {
                Permissions = new List<Permission> { newPermission }
            };
            permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup2 }).ToArray();
            await vhContext.Init(objectTypes, permissions, permissionGroups);
            await using (VhContext vhContext2 = new())
            {
                Assert.IsTrue(vhContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup2.PermissionGroupId));
                Assert.IsFalse(vhContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId));
            }

        }

        [TestMethod]
        public async Task Role()
        {
            //-----------
            // check: assigned Role permission should remain intact after renaming permission group
            //-----------

            //-----------
            // check: used SecureObjectType should not be deleted
            //-----------
        }

    }
}