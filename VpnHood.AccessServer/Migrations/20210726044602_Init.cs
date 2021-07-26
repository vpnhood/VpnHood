using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VpnHood.AccessServer.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.ClientId);
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
                    SettingsId = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.SettingsId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthUserId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ServerEndPointGroups",
                columns: table => new
                {
                    ServerEndPointGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerEndPointGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPointGroups", x => x.ServerEndPointGroupId);
                    table.ForeignKey(
                        name: "FK_ServerEndPointGroups_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastStatusTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastSessionCount = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_Servers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                columns: table => new
                {
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Secret = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false, defaultValueSql: "(Crypt_Gen_Random((16)))"),
                    ServerEndPointGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxTraffic = table.Column<long>(type: "bigint", nullable: false),
                    Lifetime = table.Column<int>(type: "int", nullable: false),
                    MaxClient = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.AccessTokenId);
                    table.ForeignKey(
                        name: "FK_AccessTokens_ServerEndPointGroups_ServerEndPointGroupId",
                        column: x => x.ServerEndPointGroupId,
                        principalTable: "ServerEndPointGroups",
                        principalColumn: "ServerEndPointGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerEndPoints",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerEndPointId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LocalEndPoint = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ServerEndPointGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertificateRawData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPoints", x => new { x.AccountId, x.ServerEndPointId });
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId");
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_ServerEndPointGroups_ServerEndPointGroupId",
                        column: x => x.ServerEndPointGroupId,
                        principalTable: "ServerEndPointGroups",
                        principalColumn: "ServerEndPointGroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "ServerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessUsageLogs",
                columns: table => new
                {
                    AccessUsageLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClientVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessUsageLogs", x => x.AccessUsageLogId);
                    table.ForeignKey(
                        name: "FK_AccessUsageLogs_AccessTokens_AccessTokenId",
                        column: x => x.AccessTokenId,
                        principalTable: "AccessTokens",
                        principalColumn: "AccessTokenId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessUsageLogs_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessUsages",
                columns: table => new
                {
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", maxLength: 20, nullable: false),
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    ConnectTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    ModifiedTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessUsages", x => new { x.AccessTokenId, x.ClientId });
                    table.ForeignKey(
                        name: "FK_AccessUsages_AccessTokens_AccessTokenId",
                        column: x => x.AccessTokenId,
                        principalTable: "AccessTokens",
                        principalColumn: "AccessTokenId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessUsages_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_ServerEndPointGroupId",
                table: "AccessTokens",
                column: "ServerEndPointGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_SupportId",
                table: "AccessTokens",
                column: "SupportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsageLogs_AccessTokenId",
                table: "AccessUsageLogs",
                column: "AccessTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsageLogs_ClientId",
                table: "AccessUsageLogs",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessUsages_ClientId",
                table: "AccessUsages",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPointGroups_AccountId_ServerEndPointGroupName",
                table: "ServerEndPointGroups",
                columns: new[] { "AccountId", "ServerEndPointGroupName" },
                unique: true,
                filter: "[ServerEndPointGroupName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_AccountId_LocalEndPoint",
                table: "ServerEndPoints",
                columns: new[] { "AccountId", "LocalEndPoint" },
                unique: true,
                filter: "[LocalEndPoint] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_ServerEndPointGroupId_IsDefault",
                table: "ServerEndPoints",
                columns: new[] { "ServerEndPointGroupId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_ServerId",
                table: "ServerEndPoints",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_AccountId_ServerName",
                table: "Servers",
                columns: new[] { "AccountId", "ServerName" },
                unique: true,
                filter: "[ServerName] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessUsageLogs");

            migrationBuilder.DropTable(
                name: "AccessUsages");

            migrationBuilder.DropTable(
                name: "PublicCycles");

            migrationBuilder.DropTable(
                name: "ServerEndPoints");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AccessTokens");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "ServerEndPointGroups");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
