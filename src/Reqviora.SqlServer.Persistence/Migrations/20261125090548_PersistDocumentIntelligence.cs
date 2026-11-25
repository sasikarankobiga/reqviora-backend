using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistDocumentIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "ProjectDocuments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "ProjectDocuments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParseErrorCode",
                table: "ProjectDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParseLeaseExpiresAtUnixTimeMilliseconds",
                table: "ProjectDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParsedAt",
                table: "ProjectDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsingStatus",
                table: "ProjectDocuments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "ParsingWarningsJson",
                table: "ProjectDocuments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "ProjectDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "ProjectDocuments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ProjectDocumentSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Locator = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    StartOffset = table.Column<int>(type: "int", nullable: false),
                    EndOffset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocumentSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDocumentSegments_ProjectDocuments_ProjectDocumentId",
                        column: x => x.ProjectDocumentId,
                        principalTable: "ProjectDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_ParsingStatus",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "ParsingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_Sha256",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "Sha256" },
                unique: true,
                filter: "[Sha256] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentSegments_ProjectDocumentId_Sequence",
                table: "ProjectDocumentSegments",
                columns: new[] { "ProjectDocumentId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectDocumentSegments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocuments_ProjectId_ParsingStatus",
                table: "ProjectDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocuments_ProjectId_Sha256",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ParseErrorCode",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ParseLeaseExpiresAtUnixTimeMilliseconds",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ParsedAt",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ParsingStatus",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ParsingWarningsJson",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "ProjectDocuments");
        }
    }
}
