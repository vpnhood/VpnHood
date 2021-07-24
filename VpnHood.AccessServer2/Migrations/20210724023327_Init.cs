using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VpnHood.AccessServer2.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LastConnectTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
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
                name: "ServerEndPointGroups",
                columns: table => new
                {
                    ServerEndPointGroupId = table.Column<int>(type: "int", nullable: false),
                    ServerEndPointGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPointGroups", x => x.ServerEndPointGroupId);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastStatusTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    LastSessionCount = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.ServerId);
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
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthUserId = table.Column<string>(type: "nchar(40)", fixedLength: true, maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UsageLogs",
                columns: table => new
                {
                    UsageLogId = table.Column<long>(type: "bigint", nullable: false)
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
                    CreatedTime = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLogs", x => x.UsageLogId);
                    table.ForeignKey(
                        name: "FK_UsageLogs_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                columns: table => new
                {
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    AccessTokenName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Secret = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false, defaultValueSql: "(Crypt_Gen_Random((16)))"),
                    ServerEndPointGroupId = table.Column<int>(type: "int", nullable: false),
                    MaxTraffic = table.Column<long>(type: "bigint", nullable: false),
                    Lifetime = table.Column<int>(type: "int", nullable: false),
                    MaxClient = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.AccessTokenId);
                    table.ForeignKey(
                        name: "FK_AccessTokens_ServerEndPointGroups_ServerEndPointGroupId",
                        column: x => x.ServerEndPointGroupId,
                        principalTable: "ServerEndPointGroups",
                        principalColumn: "ServerEndPointGroupId");
                });

            migrationBuilder.CreateTable(
                name: "ServerEndPoints",
                columns: table => new
                {
                    ServerEndPointId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    ServerEndPointGroupId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertificateRawData = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndPoints", x => x.ServerEndPointId);
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_ServerEndPointGroups_ServerEndPointGroupId",
                        column: x => x.ServerEndPointGroupId,
                        principalTable: "ServerEndPointGroups",
                        principalColumn: "ServerEndPointGroupId");
                    table.ForeignKey(
                        name: "FK_ServerEndPoints_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "ServerId");
                });

            migrationBuilder.CreateTable(
                name: "AccessUsages",
                columns: table => new
                {
                    AccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CycleSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    CycleReceivedTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalSentTraffic = table.Column<long>(type: "bigint", nullable: false),
                    TotalReceivedTraffic = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessUsages", x => new { x.AccessTokenId, x.ClientIp });
                    table.ForeignKey(
                        name: "FK_AccessUsages_AccessTokens_AccessTokenId",
                        column: x => x.AccessTokenId,
                        principalTable: "AccessTokens",
                        principalColumn: "AccessTokenId",
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
                name: "IX_ServerEndPointGroups_ServerEndPointGroupName",
                table: "ServerEndPointGroups",
                column: "ServerEndPointGroupName",
                unique: true,
                filter: "[ServerEndPointGroupName] IS NOT NULL");

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
                name: "IX_Servers_ServerName",
                table: "Servers",
                column: "ServerName",
                unique: true,
                filter: "[ServerName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLogs_ClientId",
                table: "UsageLogs",
                column: "ClientId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessUsages");

            migrationBuilder.DropTable(
                name: "PublicCycles");

            migrationBuilder.DropTable(
                name: "ServerEndPoints");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "UsageLogs");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "AccessTokens");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "ServerEndPointGroups");
        }
    }
}
