using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportIncidentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportIncidents_Users_AssignedAdminId",
                        column: x => x.AssignedAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportIncidents_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SupportIncidentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreviousValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportIncidentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportIncidentHistory_SupportIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "SupportIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportIncidentHistory_Users_ChangedBy",
                        column: x => x.ChangedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidentHistory_ChangedBy",
                table: "SupportIncidentHistory",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidentHistory_IncidentId_ChangedAtUnixTimeMillisec~",
                table: "SupportIncidentHistory",
                columns: new[] { "IncidentId", "ChangedAtUnixTimeMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidents_AssignedAdminId",
                table: "SupportIncidents",
                column: "AssignedAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidents_Reference",
                table: "SupportIncidents",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidents_ReporterId",
                table: "SupportIncidents",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportIncidents_Status_Priority_UpdatedAtUnixTimeMilliseco~",
                table: "SupportIncidents",
                columns: new[] { "Status", "Priority", "UpdatedAtUnixTimeMilliseconds" });

            migrationBuilder.Sql("""
                ALTER TABLE public."SupportIncidents" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."SupportIncidentHistory" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."SupportIncidents", public."SupportIncidentHistory" FROM PUBLIC;
                DO $private_tables$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'SupportIncidents', role_name);
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'SupportIncidentHistory', role_name);
                    END LOOP;
                END;
                $private_tables$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportIncidentHistory");

            migrationBuilder.DropTable(
                name: "SupportIncidents");
        }
    }
}
