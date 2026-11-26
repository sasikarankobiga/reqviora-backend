using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentAiJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Result = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LeaseExpiresAtUnixTimeMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiJobs_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_OwnerId_ProjectId_IdempotencyKey",
                table: "AiJobs",
                columns: new[] { "OwnerId", "ProjectId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_ProjectId",
                table: "AiJobs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_State_AvailableAtUnixTimeMilliseconds_LeaseExpiresAt~",
                table: "AiJobs",
                columns: new[] { "State", "AvailableAtUnixTimeMilliseconds", "LeaseExpiresAtUnixTimeMilliseconds" });

            migrationBuilder.Sql("""
                ALTER TABLE public."AiJobs" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."AiJobs" FROM PUBLIC;
                DO $private_ai_jobs$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', 'AiJobs', role_name);
                    END LOOP;
                END;
                $private_ai_jobs$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiJobs");
        }
    }
}
