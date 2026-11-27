using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
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
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefundRequests", x => x.PaymentId);
                    table.CheckConstraint("CK_RefundRequests_Reason", "[Reason] IN ('duplicate_payment','service_issue','other')");
                    table.CheckConstraint("CK_RefundRequests_Status", "[Status] IN ('requested','reviewed','rejected')");
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefundRequests");
        }
    }
}
