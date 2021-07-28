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
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()")
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
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()")
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
                    SettingId = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false)
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
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokenGroups", x => x.AccessTokenGroupId);
                    table.ForeignKey(
                        name: "FK_AccessTokenGroups_Accounts_AccountId",
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
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    LastStatusTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    LastSessionCount = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0"),
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
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newid()"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                        name: "FK_AccessTokens_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "ServerEndPoints",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PulicEndPoint = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PrivateEndPoint = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccessTokenGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertificateRawData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPoints", x => new { x.AccountId, x.PulicEndPoint });
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_AccessTokenGroups_AccessTokenGroupId",
                        column: x => x.AccessTokenGroupId,
                        principalTable: "AccessTokenGroups",
                        principalColumn: "AccessTokenGroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId");
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
                    ClientIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClientVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    ConnectTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()"),
                    ModifiedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "getdate()")
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
                name: "IX_AccessTokenGroups_AccountId_AccessTokenGroupName",
                table: "AccessTokenGroups",
                columns: new[] { "AccountId", "AccessTokenGroupName" },
                unique: true,
                filter: "[AccessTokenGroupName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_AccessTokenGroupId",
                table: "AccessTokens",
                column: "AccessTokenGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_AccountId_SupportCode",
                table: "AccessTokens",
                columns: new[] { "AccountId", "SupportCode" },
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
                name: "IX_ServerEndPoints_AccessTokenGroupId_IsDefault",
                table: "ServerEndPoints",
                columns: new[] { "AccessTokenGroupId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndPoints_AccountId_PrivateEndPoint",
                table: "ServerEndPoints",
                columns: new[] { "AccountId", "PrivateEndPoint" },
                unique: true,
                filter: "PrivateEndPoint IS NOT NULL");

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
                name: "AccessTokenGroups");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
