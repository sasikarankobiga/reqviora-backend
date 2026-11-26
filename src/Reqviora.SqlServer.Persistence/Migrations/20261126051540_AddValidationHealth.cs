using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAtMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    UtcDay = table.Column<int>(type: "int", nullable: false),
                    Workflow = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    SchemaFailures = table.Column<int>(type: "int", nullable: false),
                    BrokenReferences = table.Column<int>(type: "int", nullable: false),
                    UnsupportedComponents = table.Column<int>(type: "int", nullable: false),
                    NavigationFailures = table.Column<int>(type: "int", nullable: false),
                    OtherFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationTelemetry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationTelemetry_UtcDay",
                table: "ValidationTelemetry",
                column: "UtcDay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValidationTelemetry");
        }
    }
}
