using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

internal static class BillingPersistenceConfiguration
{
    internal static void Configure(
        ModelBuilder model,
        bool sqlServer)
    {
        var refund = model.Entity<PaymentRefundRequest>();

        var refundStatus = sqlServer ? "[Status]" : "\"Status\"";
        var refundReason = sqlServer ? "[Reason]" : "\"Reason\"";

        refund.ToTable("PaymentRefundRequests", table =>
        {
            table.HasCheckConstraint("CK_RefundRequests_Status",
                $"{refundStatus} IN ('requested','reviewed','rejected')");
            table.HasCheckConstraint("CK_RefundRequests_Reason",
                $"{refundReason} IN ('duplicate_payment','service_issue','other')");
        });

        refund.HasKey(x => x.PaymentId);
        refund.Property(x => x.PaymentId).ValueGeneratedNever();
        refund.Property(x => x.Status).HasMaxLength(20).IsRequired();
        refund.Property(x => x.Reason).HasMaxLength(30).IsRequired();
        refund.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        refund.HasIndex(x => x.Status);

        refund.HasOne<ProjectPaymentOrder>().WithOne()
            .HasForeignKey<PaymentRefundRequest>(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        refund.HasOne<User>().WithMany()
            .HasForeignKey(x => x.RequestedBy)
            .OnDelete(DeleteBehavior.NoAction);

        refund.HasOne<User>().WithMany()
            .HasForeignKey(x => x.ReviewedBy)
            .OnDelete(DeleteBehavior.NoAction);

        var order = model.Entity<ProjectPaymentOrder>();

        var amountColumn =
            sqlServer ? "[Amount]" : "\"Amount\"";

        var currencyColumn =
            sqlServer ? "[Currency]" : "\"Currency\"";

        var statusColumn =
            sqlServer ? "[Status]" : "\"Status\"";

        order.ToTable("ProjectPaymentOrders", table =>
        {
            table.HasCheckConstraint(
                "CK_ProjectPaymentOrders_Amount",
                $"{amountColumn} > 0 AND {amountColumn} <= 999999999.99");

            table.HasCheckConstraint(
                "CK_ProjectPaymentOrders_Currency",
                $"{currencyColumn} IN ('USD', 'LKR')");

            table.HasCheckConstraint(
                "CK_ProjectPaymentOrders_Status",
                $"{statusColumn} IN (0, 1, 2, 3, 4)");
        });

        order.HasKey(x => x.Id);

        order.Property(x => x.Amount)
            .HasPrecision(11, 2)
            .IsRequired();

        order.Property(x => x.Currency)
            .HasMaxLength(3)
            .IsUnicode(false)
            .IsRequired();

        order.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        order.Property(x => x.GatewayPaymentId)
            .HasMaxLength(128);

        order.Property(x => x.ConcurrencyToken)
            .IsConcurrencyToken();

        order.Property(x => x.CreatedAt)
            .IsRequired();

        order.Property(x => x.UpdatedAt)
            .IsRequired();

        order.Ignore(x => x.OrderReference);
        order.Ignore(x => x.IsSettled);

        order.HasIndex(x => x.GatewayPaymentId)
            .IsUnique()
            .HasFilter(
                sqlServer
                    ? "[GatewayPaymentId] IS NOT NULL"
                    : "\"GatewayPaymentId\" IS NOT NULL");

        order.HasIndex(x => new
        {
            x.ProjectId,
            x.Status
        });

        order.HasIndex(x => new
        {
            x.PayerId,
            x.CreatedAt
        });

        order.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.NoAction);

        order.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.PayerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
