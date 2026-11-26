using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettingVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettingRevisions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PreviousValue = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RolledBackTo = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettingRevisions", x => new { x.Key, x.Revision });
                    table.ForeignKey(
                        name: "FK_PlatformSettingRevisions_Users_ChangedBy",
                        column: x => x.ChangedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettingRevisions_ChangedBy",
                table: "PlatformSettingRevisions",
                column: "ChangedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettingRevisions");
        }
    }
}
