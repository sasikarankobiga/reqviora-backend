using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistVoiceTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceTranscripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudioFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudioStoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    EditableText = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceTranscripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceTranscripts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoiceTranscripts_Users_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VoiceTranscriptSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoiceTranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StartMs = table.Column<long>(type: "bigint", nullable: false),
                    EndMs = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceTranscriptSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceTranscriptSegments_VoiceTranscripts_VoiceTranscriptId",
                        column: x => x.VoiceTranscriptId,
                        principalTable: "VoiceTranscripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTranscripts_AudioFileId",
                table: "VoiceTranscripts",
                column: "AudioFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTranscripts_ProjectId_CreatedAt",
                table: "VoiceTranscripts",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTranscripts_ReviewedBy",
                table: "VoiceTranscripts",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTranscriptSegments_VoiceTranscriptId_Sequence",
                table: "VoiceTranscriptSegments",
                columns: new[] { "VoiceTranscriptId", "Sequence" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE public."VoiceTranscripts" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."VoiceTranscriptSegments" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."VoiceTranscripts" FROM PUBLIC;
                REVOKE ALL ON TABLE public."VoiceTranscriptSegments" FROM PUBLIC;
                DO $private_voice_transcripts$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format(
                            'REVOKE ALL ON TABLE public.%I FROM %I',
                            'VoiceTranscripts', role_name);
                        EXECUTE format(
                            'REVOKE ALL ON TABLE public.%I FROM %I',
                            'VoiceTranscriptSegments', role_name);
                    END LOOP;
                END;
                $private_voice_transcripts$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceTranscriptSegments");

            migrationBuilder.DropTable(
                name: "VoiceTranscripts");
        }
    }
}
