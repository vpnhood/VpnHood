using System;
using VpnHood.AccessServer.MultiLevelAuthorization.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;

namespace VpnHood.AccessServer.Security;

public static class SecureObjectTypes
{
    public static SecureObjectType System { get; } = MultilevelAuthRepo.SystemSecureObjectType;
    public static SecureObjectType Project { get; } = new(Guid.Parse("{6FE94D89-632D-40C9-9176-30878F830AEE}"), nameof(Project));
    public static SecureObjectType User { get; } = new(Guid.Parse("{CECF8DFF-8ED2-43E4-ACA4-BA5607C5B037}"), nameof(User));

    public static SecureObjectType[] All { get; } = { System, Project, User };
}