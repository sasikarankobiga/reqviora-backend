using Reqviora.Api.Features.Billing;
using Reqviora.Application.Features.Billing;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProjectPaymentGateTests
{
    private const string Cutoff =
        "2026-11-26T00:00:00Z";

    private static ProjectBillingSnapshot Snapshot(
        bool paid = false,
        bool pending = false,
        bool existing = false)
    {
        return new(
            Guid.NewGuid(),
            new DateTimeOffset(
                2026, 11, existing ? 25 : 27,
                0, 0, 0, TimeSpan.Zero),
            paid,
            pending);
    }

    [Fact]
    public void Disabled_enforcement_preserves_existing_access()
    {
        Assert.Equal(
            BillingGateDecision.Allowed,
            ProjectBillingPolicy.Evaluate(
                Snapshot(), false, true, null));
    }

    [Fact]
    public void Unpaid_project_requires_payment()
    {
        Assert.Equal(
            BillingGateDecision.PaymentRequired,
            ProjectBillingPolicy.Evaluate(
                Snapshot(), true, true, Cutoff));
    }

    [Fact]
    public void Pending_payment_does_not_unlock_project()
    {
        Assert.Equal(
            BillingGateDecision.PaymentRequired,
            ProjectBillingPolicy.Evaluate(
                Snapshot(pending: true),
                true, true, Cutoff));
    }

    [Fact]
    public void Paid_project_is_allowed()
    {
        Assert.Equal(
            BillingGateDecision.Allowed,
            ProjectBillingPolicy.Evaluate(
                Snapshot(paid: true),
                true, true, Cutoff));
    }

    [Fact]
    public void Existing_project_is_grandfathered()
    {
        Assert.Equal(
            BillingGateDecision.Grandfathered,
            ProjectBillingPolicy.Evaluate(
                Snapshot(existing: true),
                true, true, Cutoff));
    }

    [Fact]
    public void Missing_cutoff_fails_closed()
    {
        Assert.Equal(
            BillingGateDecision.Unavailable,
            ProjectBillingPolicy.Evaluate(
                Snapshot(), true, true, null));
    }

    [Fact]
    public void Sandbox_entitlement_cannot_activate_in_production()
    {
        Assert.Equal(
            BillingGateDecision.Unavailable,
            ProjectBillingPolicy.Evaluate(
                Snapshot(paid: true),
                true, false, Cutoff));
    }
}
