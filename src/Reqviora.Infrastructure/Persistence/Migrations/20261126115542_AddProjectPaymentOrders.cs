using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPaymentOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectPaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(11,2)", precision: 11, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: false),
                    GatewayPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPaymentOrders", x => x.Id);
                    table.CheckConstraint("CK_ProjectPaymentOrders_Amount", "\"Amount\" > 0 AND \"Amount\" <= 999999999.99");
                    table.CheckConstraint("CK_ProjectPaymentOrders_Currency", "\"Currency\" IN ('USD', 'LKR')");
                    table.CheckConstraint("CK_ProjectPaymentOrders_Status", "\"Status\" IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ProjectPaymentOrders_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectPaymentOrders_Users_PayerId",
                        column: x => x.PayerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPaymentOrders_GatewayPaymentId",
                table: "ProjectPaymentOrders",
                column: "GatewayPaymentId",
                unique: true,
                filter: "\"GatewayPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPaymentOrders_PayerId_CreatedAt",
                table: "ProjectPaymentOrders",
                columns: new[] { "PayerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPaymentOrders_ProjectId_Status",
                table: "ProjectPaymentOrders",
                columns: new[] { "ProjectId", "Status" });
            migrationBuilder.Sql("""
                ALTER TABLE public."ProjectPaymentOrders" ENABLE ROW LEVEL SECURITY;

                REVOKE ALL ON TABLE public."ProjectPaymentOrders" FROM PUBLIC;

                DO $private_table$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN
                        SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format(
                            'REVOKE ALL ON TABLE public.%I FROM %I',
                            'ProjectPaymentOrders',
                            role_name
                        );
                    END LOOP;
                END;
                $private_table$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectPaymentOrders");
        }
    }
}
