using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentRefundRequests",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefundRequests", x => x.PaymentId);
                    table.CheckConstraint("CK_RefundRequests_Reason", "\"Reason\" IN ('duplicate_payment','service_issue','other')");
                    table.CheckConstraint("CK_RefundRequests_Status", "\"Status\" IN ('requested','reviewed','rejected')");
                    table.ForeignKey(
                        name: "FK_PaymentRefundRequests_ProjectPaymentOrders_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "ProjectPaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentRefundRequests_Users_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentRefundRequests_Users_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefundRequests_RequestedBy",
                table: "PaymentRefundRequests",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefundRequests_ReviewedBy",
                table: "PaymentRefundRequests",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefundRequests_Status",
                table: "PaymentRefundRequests",
                column: "Status");
            migrationBuilder.Sql("""
                ALTER TABLE public."PaymentRefundRequests" ENABLE ROW LEVEL SECURITY;
                REVOKE ALL ON TABLE public."PaymentRefundRequests" FROM PUBLIC;

                DO $private_table$
                DECLARE role_name text;
                BEGIN
                    FOR role_name IN
                        SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon','authenticated','service_role')
                    LOOP
                        EXECUTE format(
                            'REVOKE ALL ON TABLE public.%I FROM %I',
                            'PaymentRefundRequests', role_name);
                    END LOOP;
                END;
                $private_table$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefundRequests");
        }
    }
}
