using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VpnHood.AccessServer.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "PublicCycles",
                columns: table => new
                {
                    PublicCycleId = table.Column<string>(type: "nchar(12)", fixedLength: true, maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCycles", x => x.PublicCycleId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    SettingId = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false),
                    Reserved1 = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.SettingId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    AuthUserId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "AccessTokenGroups",
                columns: table => new
                {
                    AccessTokenGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokenGroups", x => x.AccessTokenGroupId);
                    table.ForeignKey(
                        name: "FK_AccessTokenGroups_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    ClientKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientIp = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.ClientKeyId);
                    table.ForeignKey(
                        name: "FK_Clients_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId");
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnvironmentVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OsVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalMemory = table.Column<long>(type: "bigint", nullable: false),
                    LocalIp = table.Column<string>(type: "nvarchar(45)", nullable: true),
                    PublicIp = table.Column<string>(type: "nvarchar(45)", nullable: true),
                    SubscribeTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_Servers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                columns: table => new
                {
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupportCode = table.Column<int>(type: "int", nullable: false),
                    Secret = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false, defaultValueSql: "Crypt_Gen_Random((16))"),
                    AccessTokenGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    Lifetime = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    MaxClient = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
                    StartTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.AccessTokenId);
                    table.ForeignKey(
                        name: "FK_AccessTokens_AccessTokenGroups_AccessTokenGroupId",
                        column: x => x.AccessTokenGroupId,
                        principalTable: "AccessTokenGroups",
                        principalColumn: "AccessTokenGroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessTokens_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId");
                });

            migrationBuilder.CreateTable(
                name: "ServerEndPoints",
                columns: table => new
                {
                    ServerEndPointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PulicEndPoint = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PrivateEndPoint = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccessTokenGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertificateRawData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPoints", x => x.ServerEndPointId);
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_AccessTokenGroups_AccessTokenGroupId",
                        column: x => x.AccessTokenGroupId,
                        principalTable: "AccessTokenGroups",
                        principalColumn: "AccessTokenGroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId");
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "ServerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerStatusLogs",
                columns: table => new
                {
                    ServerStatusLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    NatTcpCount = table.Column<int>(type: "int", nullable: false),
                    NatUdpCount = table.Column<int>(type: "int", nullable: false),
                    FreeMemory = table.Column<int>(type: "int", nullable: false),
                    ThreadCount = table.Column<int>(type: "int", nullable: false),
                    IsSubscribe = table.Column<bool>(type: "bit", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    IsLast = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerStatusLogs", x => x.ServerStatusLogId);
                    table.ForeignKey(
                        name: "FK_ServerStatusLogs_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "ServerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessUsages",
                columns: table => new
                {
                    AccessUsageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientKeyId = table.Column<Guid>(type: "uniqueidentifier", maxLength: 20, nullable: true),
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    ConnectTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    ModifiedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessUsages", x => x.AccessUsageId);
                    table.ForeignKey(
                        name: "FK_AccessUsages_AccessTokens_AccessTokenId",
                        column: x => x.AccessTokenId,
                        principalTable: "AccessTokens",
                        principalColumn: "AccessTokenId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessUsages_Clients_ClientKeyId",
                        column: x => x.ClientKeyId,
                        principalTable: "Clients",
                        principalColumn: "ClientKeyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessUsageLogs",
                columns: table => new
                {
                    AccessUsageLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessUsageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientIp = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ClientVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    ReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessUsageLogs", x => x.AccessUsageLogId);
                    table.ForeignKey(
                        name: "FK_AccessUsageLogs_AccessUsages_AccessUsageId",
                        column: x => x.AccessUsageId,
                        principalTable: "AccessUsages",
                        principalColumn: "AccessUsageId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessUsageLogs_Clients_ClientKeyId",
                        column: x => x.ClientKeyId,
                        principalTable: "Clients",
                        principalColumn: "ClientKeyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessUsageLogs_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "ServerId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokenGroups_ProjectId_AccessTokenGroupName",
                table: "AccessTokenGroups",
                columns: new[] { "ProjectId", "AccessTokenGroupName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokenGroups_ProjectId_IsDefault",
                table: "AccessTokenGroups",
                columns: new[] { "ProjectId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_AccessTokenGroupId",
                table: "AccessTokens",
                column: "AccessTokenGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_ProjectId_SupportCode",
                table: "AccessTokens",
                columns: new[] { "ProjectId", "SupportCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsageLogs_AccessUsageId",
                table: "AccessUsageLogs",
                column: "AccessUsageId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsageLogs_ClientKeyId",
                table: "AccessUsageLogs",
                column: "ClientKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsageLogs_ServerId",
                table: "AccessUsageLogs",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsages_AccessTokenId_ClientKeyId",
                table: "AccessUsages",
                columns: new[] { "AccessTokenId", "ClientKeyId" },
                unique: true,
                filter: "[ClientKeyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsages_ClientKeyId",
                table: "AccessUsages",
                column: "ClientKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ClientId",
                table: "Clients",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ProjectId_ClientId",
                table: "Clients",
                columns: new[] { "ProjectId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_AccessTokenGroupId_IsDefault",
                table: "ServerEndPoints",
                columns: new[] { "AccessTokenGroupId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_ProjectId_PrivateEndPoint",
                table: "ServerEndPoints",
                columns: new[] { "ProjectId", "PrivateEndPoint" },
                unique: true,
                filter: "PrivateEndPoint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_ProjectId_PulicEndPoint",
                table: "ServerEndPoints",
                columns: new[] { "ProjectId", "PulicEndPoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_ServerId",
                table: "ServerEndPoints",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ProjectId_ServerName",
                table: "Servers",
                columns: new[] { "ProjectId", "ServerName" },
                unique: true,
                filter: "ServerName IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatusLogs_ServerId_IsLast",
                table: "ServerStatusLogs",
                columns: new[] { "ServerId", "IsLast" },
                unique: true,
                filter: "IsLast = 1");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessUsageLogs");

            migrationBuilder.DropTable(
                name: "PublicCycles");

            migrationBuilder.DropTable(
                name: "ServerEndPoints");

            migrationBuilder.DropTable(
                name: "ServerStatusLogs");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AccessUsages");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "AccessTokens");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "AccessTokenGroups");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
