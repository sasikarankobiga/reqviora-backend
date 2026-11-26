using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedRecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    PendingRecipientKey = table.Column<string>(type: "character varying(353)", maxLength: 353, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersedesInvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectInvitations", x => x.Id);
                    table.CheckConstraint("CK_ProjectInvitations_Role", "\"Role\" IN ('Viewer','Editor')");
                    table.CheckConstraint("CK_ProjectInvitations_Status", "\"Status\" IN ('Pending','Accepted','Rejected','Revoked','Expired')");
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
                filter: "\"PendingRecipientKey\" IS NOT NULL");

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

            migrationBuilder.Sql("""
                ALTER TABLE public."ProjectInvitations" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."ProjectInvitations" FROM PUBLIC;
                DO $private_table$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ProjectInvitations', role_name);
                    END LOOP;
                END;
                $private_table$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectInvitations");
        }
    }
}
