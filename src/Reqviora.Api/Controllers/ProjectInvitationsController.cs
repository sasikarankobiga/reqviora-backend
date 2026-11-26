using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Sharing;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("invitations")]
[Route("api/projects/{projectId:guid}/invitations")]
public sealed class ProjectInvitationsController(IProjectInvitationService invitations) : ControllerBase
{
    public sealed record CreateProjectInvitationRequest(
        [Required, EmailAddress, StringLength(320)] string RecipientEmail,
        [Required, StringLength(30)] string Role);

    [HttpPost]
    public async Task<ActionResult<ProjectInvitationDetails>> Create(Guid projectId,
        CreateProjectInvitationRequest request, CancellationToken ct)
    {
        try
        {
            var invitation = await invitations.CreateAsync(projectId, UserId(), request.RecipientEmail,
                request.Role, ct);
            return StatusCode(StatusCodes.Status201Created, invitation);
        }
        catch (EmailDeliveryException)
        {
            return Problem(title: "The invitation was created, but email delivery is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet]
    public Task<IReadOnlyList<ProjectInvitationDetails>> List(Guid projectId, CancellationToken ct) =>
        invitations.ListAsync(projectId, UserId(), ct);

    [HttpPost("{invitationId:guid}/revoke")]
    public Task<ProjectInvitationDetails> Revoke(Guid projectId, Guid invitationId, CancellationToken ct) =>
        invitations.RevokeAsync(projectId, invitationId, UserId(), ct);

    [HttpPost("{invitationId:guid}/resend")]
    public async Task<ActionResult<ProjectInvitationDetails>> Resend(Guid projectId, Guid invitationId,
        CancellationToken ct)
    {
        try
        {
            return Ok(await invitations.ResendAsync(projectId, invitationId, UserId(), ct));
        }
        catch (EmailDeliveryException)
        {
            return Problem(title: "The invitation was renewed, but email delivery is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private Guid UserId() => ProjectApiAccess.RequireUserId(User);
}

[ApiController]
[Authorize]
[EnableRateLimiting("invitations")]
[Route("api/project-invitations")]
public sealed class InvitationResponsesController(IProjectInvitationService invitations) : ControllerBase
{
    public sealed record InvitationTokenRequest([Required, StringLength(512, MinimumLength = 32)] string Token);

    [HttpPost("accept")]
    public Task<AcceptedProjectMembership> Accept(InvitationTokenRequest request, CancellationToken ct) =>
        invitations.AcceptAsync(request.Token, ProjectApiAccess.RequireUserId(User), ct);

    [HttpPost("reject")]
    public Task<ProjectInvitationDetails> Reject(InvitationTokenRequest request, CancellationToken ct) =>
        invitations.RejectAsync(request.Token, ProjectApiAccess.RequireUserId(User), ct);
}
