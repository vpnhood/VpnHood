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
            SecureObjectType newSecureObjectType1 = new(Guid.NewGuid(), Guid.NewGuid().ToString());
            SecureObjectType[] secureObjectTypes = { newSecureObjectType1 };

            var maxPermissionId = vhContext.Permissions.Max(x => (int?) x.PermissionId) ?? 100;
            Permission newPermission = new(maxPermissionId + 1, Guid.NewGuid().ToString());
            Permission[] permissions = Permissions.All.Concat(new[] { newPermission }).ToArray();

            PermissionGroup newPermissionGroup1 = new(Guid.NewGuid(), Guid.NewGuid().ToString())
            {
                Permissions = new List<Permission> { newPermission }
            };
            PermissionGroup[] permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup1 }).ToArray();
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
            secureObjectTypes = new[] { newSecureObjectType2 };
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