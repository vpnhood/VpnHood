using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.MultiLevelAuthorization.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Persistence;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AuthorizationTest
{
    [TestMethod]
    public async Task Seeding()
    {
        var testInit = await TestInit.Create();
        var authRepo = testInit.Scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();

        // Create new base types
        var newSecureObjectType1 = new SecureObjectType(Guid.NewGuid(), Guid.NewGuid().ToString());
        var secureObjectTypes = SecureObjectTypes.All.Concat(new[] { newSecureObjectType1 }).ToArray();

        var maxPermissionId = 100;
        var newPermission = new Permission(maxPermissionId + 1, Guid.NewGuid().ToString());
        var permissions = Permissions.All.Concat(new[] { newPermission }).ToArray();

        var newPermissionGroup1 = new PermissionGroup(Guid.NewGuid(), Guid.NewGuid().ToString())
        {
            Permissions = new List<Permission> { newPermission }
        };
        var permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup1 }).ToArray();
        await authRepo.Init(secureObjectTypes, permissions, permissionGroups);

        await using (var scope2 = testInit.WebApp.Services.CreateAsyncScope())
        await using (var authContext = scope2.ServiceProvider.GetRequiredService<MultilevelAuthContext>())
        {

            //-----------
            // check: new type is inserted
            //-----------
            Assert.AreEqual(newSecureObjectType1.SecureObjectTypeName,
                authContext.SecureObjectTypes.Single(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId).SecureObjectTypeName);

            //-----------
            // check: new permission is inserted
            //-----------
            Assert.AreEqual(newPermission.PermissionName,
                authContext.Permissions.Single(x => x.PermissionId == newPermission.PermissionId).PermissionName);

            //-----------
            // check new permission group is inserted
            //-----------
            Assert.AreEqual(newPermissionGroup1.PermissionGroupName,
                authContext.PermissionGroups
                    .Single(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId).PermissionGroupName);

            //-----------
            // check: new permission group permissions in inserted
            //-----------
            Assert.IsTrue(authContext.PermissionGroups.Include(x => x.Permissions)
                .Single(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId)
                .Permissions.Any(x => x.PermissionId == newPermission.PermissionId));

            //-----------
            // check: System object is not deleted
            //-----------
            Assert.IsTrue(authContext.SecureObjectTypes.Any(x => x.SecureObjectTypeId == MultilevelAuthService.SystemSecureObjectTypeId));
            Assert.IsTrue(authContext.PermissionGroups.Any(x => x.PermissionGroupId == MultilevelAuthService.SystemPermissionGroupId));
        }

        //-----------
        // check: update SecureObjectTypeName
        //-----------
        newSecureObjectType1.SecureObjectTypeName = "new-name_" + Guid.NewGuid();
        await authRepo.Init(secureObjectTypes, permissions, permissionGroups);
        await using (var scope2 = testInit.WebApp.Services.CreateAsyncScope())
        await using (var vhContext2 = scope2.ServiceProvider.GetRequiredService<MultilevelAuthContext>())
            Assert.AreEqual(newSecureObjectType1.SecureObjectTypeName, vhContext2.SecureObjectTypes.Single(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId).SecureObjectTypeName);

        //-----------
        // check: add/remove SecureObjectTypeName
        //-----------
        SecureObjectType newSecureObjectType2 = new(Guid.NewGuid(), Guid.NewGuid().ToString());
        secureObjectTypes = SecureObjectTypes.All.Concat(new[] { newSecureObjectType2 }).ToArray();
        await authRepo.Init(secureObjectTypes, permissions, permissionGroups);
        await using (var scope2 = testInit.WebApp.Services.CreateAsyncScope())
        await using (var authContext = scope2.ServiceProvider.GetRequiredService<MultilevelAuthContext>())
        {
            Assert.IsTrue(authContext.SecureObjectTypes.Any(x => x.SecureObjectTypeId == newSecureObjectType2.SecureObjectTypeId));
            Assert.IsFalse(authContext.SecureObjectTypes.Any(x => x.SecureObjectTypeId == newSecureObjectType1.SecureObjectTypeId));
        }

        //-----------
        // check: add/remove new PermissionGroup
        //-----------
        PermissionGroup newPermissionGroup2 = new(Guid.NewGuid(), Guid.NewGuid().ToString())
        {
            Permissions = new List<Permission> { newPermission }
        };
        permissionGroups = PermissionGroups.All.Concat(new[] { newPermissionGroup2 }).ToArray();
        await authRepo.Init(secureObjectTypes, permissions, permissionGroups);
        await using (var scope2 = testInit.WebApp.Services.CreateAsyncScope())
        await using (var authContext2 = scope2.ServiceProvider.GetRequiredService<MultilevelAuthContext>())
        {
            Assert.IsTrue(authContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup2.PermissionGroupId));
            Assert.IsFalse(authContext2.PermissionGroups.Any(x => x.PermissionGroupId == newPermissionGroup1.PermissionGroupId));
        }

    }

    [TestMethod]
    public async Task Rename_permission_group()
    {
        var testInit = await TestInit.Create();
        var authRepo = testInit.Scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();

        var secureObject = await authRepo.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project);

        //-----------
        // check: assigned permission group should remain intact after renaming its name
        //-----------
        var guest1 = Guid.NewGuid();

        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObject.SecureObjectId, guest1, Permissions.ProjectRead));
        await authRepo.SecureObject_AddUserPermission(secureObject, guest1,
            PermissionGroups.ProjectViewer, MultilevelAuthService.SystemUserId);
        PermissionGroups.ProjectViewer.PermissionGroupName = Guid.NewGuid().ToString();
        await authRepo.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObject.SecureObjectId, guest1, Permissions.ProjectRead));

        //-----------
        // check: used SecureObjectType should not be deleted
        //-----------
        try
        {
            await using var scope = testInit.WebApp.Services.CreateAsyncScope();
            var authContext = scope.ServiceProvider.GetRequiredService<MultilevelAuthContext>();
            authContext.SecureObjectTypes.Remove(SecureObjectTypes.Project);
            await authContext.SaveChangesAsync();
            Assert.Fail("No cascade expected for SecureObjectType!");
        }
        catch { /* ignored */ }
    }

    [TestMethod]
    public async Task InheritanceAccess()
    {
        var testInit = await TestInit.Create();
        var authRepo = testInit.Scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();

        var secureObjectL1 = await authRepo.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project);
        var secureObjectL2 = await authRepo.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL1);
        var secureObjectL3 = await authRepo.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL2);
        var secureObjectL4 = await authRepo.CreateSecureObject(Guid.NewGuid(), SecureObjectTypes.Project, secureObjectL3);

        // add guest1 to Role1
        var guest1 = Guid.NewGuid();
        var role1 = await authRepo.Role_Create(testInit.ProjectId, Guid.NewGuid().ToString(), MultilevelAuthService.SystemUserId);
        await authRepo.Role_AddUser(role1.RoleId, guest1, MultilevelAuthService.SystemUserId);

        // add guest2 to Role2
        var guest2 = Guid.NewGuid();
        var role2 = await authRepo.Role_Create(testInit.ProjectId, Guid.NewGuid().ToString(), MultilevelAuthService.SystemUserId);
        await authRepo.Role_AddUser(role2.RoleId, guest2, MultilevelAuthService.SystemUserId);

        //-----------
        // check: inheritance: add role1 to L3 and it shouldn't access to L1
        //-----------
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest1, Permissions.ProjectRead));

        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest2, Permissions.ProjectRead));


        await authRepo.SecureObject_AddRolePermission(secureObjectL3, role1, PermissionGroups.ProjectViewer, MultilevelAuthService.SystemUserId);
        await authRepo.SecureObject_AddRolePermission(secureObjectL1, role2, PermissionGroups.ProjectViewer, MultilevelAuthService.SystemUserId);
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsFalse(await authRepo.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest1, Permissions.ProjectRead));
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest1, Permissions.ProjectRead));

        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL1.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL2.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL3.SecureObjectId, guest2, Permissions.ProjectRead));
        Assert.IsTrue(await authRepo.SecureObject_HasUserPermission(secureObjectL4.SecureObjectId, guest2, Permissions.ProjectRead));
    }

}