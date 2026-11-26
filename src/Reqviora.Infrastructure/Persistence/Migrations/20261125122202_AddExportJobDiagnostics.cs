using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportJobDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExportRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IntegrityStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportJobDiagnostics_ExportRecords_ExportRecordId",
                        column: x => x.ExportRecordId,
                        principalTable: "ExportRecords",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExportJobDiagnostics_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobDiagnostics_ExportRecordId",
                table: "ExportJobDiagnostics",
                column: "ExportRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobDiagnostics_ProjectId_CreatedAt",
                table: "ExportJobDiagnostics",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobDiagnostics_Status_CreatedAt",
                table: "ExportJobDiagnostics",
                columns: new[] { "Status", "CreatedAt" });
            migrationBuilder.Sql("""
                ALTER TABLE public."ExportJobDiagnostics" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."ExportJobDiagnostics" FROM PUBLIC;
                DO $private_table$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ExportJobDiagnostics', role_name);
                    END LOOP;
                END;
                $private_table$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportJobDiagnostics");
        }
    }
}
