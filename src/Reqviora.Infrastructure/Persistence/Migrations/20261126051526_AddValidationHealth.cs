using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValidationTelemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    UtcDay = table.Column<int>(type: "integer", nullable: false),
                    Workflow = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    SchemaFailures = table.Column<int>(type: "integer", nullable: false),
                    BrokenReferences = table.Column<int>(type: "integer", nullable: false),
                    UnsupportedComponents = table.Column<int>(type: "integer", nullable: false),
                    NavigationFailures = table.Column<int>(type: "integer", nullable: false),
                    OtherFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationTelemetry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationTelemetry_UtcDay",
                table: "ValidationTelemetry",
                column: "UtcDay");
            migrationBuilder.Sql("""
                ALTER TABLE public."ValidationTelemetry" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."ValidationTelemetry" FROM PUBLIC;
                DO $private_table$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'ValidationTelemetry', role_name);
                    END LOOP;
                END;
                $private_table$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValidationTelemetry");
        }
    }
}
