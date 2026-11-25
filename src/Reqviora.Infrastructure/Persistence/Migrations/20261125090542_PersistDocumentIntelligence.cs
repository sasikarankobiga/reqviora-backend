using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
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
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "ProjectDocuments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParseErrorCode",
                table: "ProjectDocuments",
                type: "character varying(100)",
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
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsingStatus",
                table: "ProjectDocuments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "ParsingWarningsJson",
                table: "ProjectDocuments",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "ProjectDocuments",
                type: "character varying(64)",
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Locator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false)
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
                filter: "\"Sha256\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentSegments_ProjectDocumentId_Sequence",
                table: "ProjectDocumentSegments",
                columns: new[] { "ProjectDocumentId", "Sequence" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE public."ProjectDocumentSegments" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."ProjectDocumentSegments" FROM PUBLIC;
                DO $private_document_segments$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format(
                            'REVOKE ALL ON TABLE public.%I FROM %I',
                            'ProjectDocumentSegments', role_name);
                    END LOOP;
                END;
                $private_document_segments$;
                """);
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
