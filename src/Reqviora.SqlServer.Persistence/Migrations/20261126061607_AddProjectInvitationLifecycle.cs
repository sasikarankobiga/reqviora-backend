using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectInvitationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NormalizedRecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PendingRecipientKey = table.Column<string>(type: "nvarchar(353)", maxLength: 353, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersedesInvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectInvitations", x => x.Id);
                    table.CheckConstraint("CK_ProjectInvitations_Role", "[Role] IN ('Viewer','Editor')");
                    table.CheckConstraint("CK_ProjectInvitations_Status", "[Status] IN ('Pending','Accepted','Rejected','Revoked','Expired')");
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_ProjectInvitations_SupersedesInvitationId",
                        column: x => x.SupersedesInvitationId,
                        principalTable: "ProjectInvitations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Users_AcceptedBy",
                        column: x => x.AcceptedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Users_RejectedBy",
                        column: x => x.RejectedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectInvitations_Users_RevokedBy",
                        column: x => x.RevokedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_AcceptedBy",
                table: "ProjectInvitations",
                column: "AcceptedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_CreatedBy",
                table: "ProjectInvitations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_NormalizedRecipientEmail_Status",
                table: "ProjectInvitations",
                columns: new[] { "NormalizedRecipientEmail", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_PendingRecipientKey",
                table: "ProjectInvitations",
                column: "PendingRecipientKey",
                unique: true,
                filter: "[PendingRecipientKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_ProjectId_CreatedAtUnixTimeMilliseconds",
                table: "ProjectInvitations",
                columns: new[] { "ProjectId", "CreatedAtUnixTimeMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_RejectedBy",
                table: "ProjectInvitations",
                column: "RejectedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_RevokedBy",
                table: "ProjectInvitations",
                column: "RevokedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_SupersedesInvitationId",
                table: "ProjectInvitations",
                column: "SupersedesInvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_TokenHash",
                table: "ProjectInvitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectInvitations");
        }
    }
}
