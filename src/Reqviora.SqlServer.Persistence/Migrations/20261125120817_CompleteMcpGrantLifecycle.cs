using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMcpGrantLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_McpAccessGrants_ApprovedRevisions_ApprovedRevisionId_ProjectId",
                        columns: x => new { x.ApprovedRevisionId, x.ProjectId },
                        principalTable: "ApprovedRevisions",
                        principalColumns: new[] { "Id", "ProjectId" });
                    table.ForeignKey(
                        name: "FK_McpAccessGrants_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_McpAccessGrants_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_McpAccessGrants_Users_RevokedBy",
                        column: x => x.RevokedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_ApprovedRevisionId_ProjectId",
                table: "McpAccessGrants",
                columns: new[] { "ApprovedRevisionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_OwnerId",
                table: "McpAccessGrants",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_ProjectId_CreatedAt",
                table: "McpAccessGrants",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_RevokedAt_ExpiresAt",
                table: "McpAccessGrants",
                columns: new[] { "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_RevokedBy",
                table: "McpAccessGrants",
                column: "RevokedBy");

            migrationBuilder.CreateIndex(
                name: "IX_McpAccessGrants_SecretHash",
                table: "McpAccessGrants",
                column: "SecretHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpAccessGrants");
        }
    }
}
