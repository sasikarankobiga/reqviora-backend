using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Reqviora.Api.Controllers;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Features.Billing;

public enum BillingGateDecision
{
    Allowed,
    Grandfathered,
    PaymentRequired,
    Unavailable
}

public static class ProjectBillingPolicy
{
    public static BillingGateDecision Evaluate(
        ProjectBillingSnapshot snapshot,
        bool enabled,
        bool development,
        string? requiredFromUtc)
    {
        if (!enabled)
            return BillingGateDecision.Allowed;

        if (!development ||
            !DateTimeOffset.TryParseExact(
                requiredFromUtc,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var cutoff))
        {
            return BillingGateDecision.Unavailable;
        }

        if (snapshot.ProjectCreatedAt < cutoff)
            return BillingGateDecision.Grandfathered;

        return snapshot.HasSuccessfulPayment
            ? BillingGateDecision.Allowed
            : BillingGateDecision.PaymentRequired;
    }
}

public sealed class ProjectPaymentGateFilter(
    IProjectBillingAccess billing,
    IConfiguration configuration,
    IWebHostEnvironment environment) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var enabled = configuration.GetValue(
            "Billing:EnforcementEnabled", false);

        if (!enabled)
        {
            await next();
            return;
        }

        var subject = context.HttpContext.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.HttpContext.User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var actorId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var projectId = Guid.Empty;

        if (context.ActionArguments.TryGetValue(
            "projectId", out var routeValue) &&
            routeValue is Guid routeId)
        {
            projectId = routeId;
        }
        else if (context.ActionArguments.TryGetValue(
            "request", out var requestValue) &&
            requestValue is McpV1Controller.ToolCallRequest request)
        {
            projectId = request.ProjectId;
        }

        if (projectId == Guid.Empty)
        {
            context.Result = new BadRequestResult();
            return;
        }

        var snapshot = await billing.ReadAsync(
            projectId,
            actorId,
            context.HttpContext.RequestAborted);

        if (snapshot is null)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var decision = ProjectBillingPolicy.Evaluate(
            snapshot,
            enabled,
            environment.IsDevelopment(),
            configuration["Billing:RequiredFromUtc"]);

        if (decision == BillingGateDecision.Unavailable)
        {
            context.Result = new ObjectResult(new
            {
                code = "billing_enforcement_unavailable"
            })
            {
                StatusCode = 503
            };

            return;
        }

        if (decision == BillingGateDecision.PaymentRequired)
        {
            context.Result = new ObjectResult(new
            {
                code = "project_payment_required",
                projectId
            })
            {
                StatusCode = 402
            };

            return;
        }

        // Existing controller authorization still runs.
        await next();
    }
}
