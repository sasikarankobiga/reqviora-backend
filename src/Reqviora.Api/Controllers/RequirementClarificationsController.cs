using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize]
[Route("api/projects/{projectId:guid}/requirements/candidates/{requirementId}/clarifications")]
public sealed class RequirementClarificationsController(ReqvioraDbContext db, IRequirementReviewStore requirements,
    IClarificationStore store) : ControllerBase
{
    public sealed record CreateQuestionRequest(string Question);
    public sealed record AnswerQuestionRequest(int ExpectedRevision, string Answer);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClarificationQuestion>>> List(Guid projectId, string requirementId,
        CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        await ProjectApiAccess.RequireReadAsync(db, projectId, ProjectApiAccess.RequireUserId(User), cancellationToken);
        if (offset < 0 || limit is < 1 or > 100) return BadRequest();
        if (await requirements.FindAsync(projectId, requirementId, cancellationToken) is null) return NotFound();
        return Ok(await store.ListAsync(projectId, requirementId, offset, limit, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ClarificationQuestion>> Create(Guid projectId, string requirementId,
        CreateQuestionRequest body, CancellationToken cancellationToken)
    {
        await ProjectApiAccess.RequireWriteAsync(db, projectId, ProjectApiAccess.RequireUserId(User), cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 2000) return BadRequest();
        if (await requirements.FindAsync(projectId, requirementId, cancellationToken) is null) return NotFound();
        var question = new ClarificationQuestion(Guid.NewGuid(), requirementId, body.Question.Trim(), null, 1, false);
        await store.InsertAsync(projectId, question, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, question);
    }

    [HttpPost("{questionId:guid}/answer")]
    public async Task<ActionResult<ClarificationQuestion>> Answer(Guid projectId, string requirementId, Guid questionId,
        AnswerQuestionRequest body, CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        if (body.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(body.Answer) || body.Answer.Length > 5000) return BadRequest();
        var current = await store.FindAsync(projectId, requirementId, questionId, cancellationToken);
        if (current is null) return NotFound();
        ClarificationQuestion updated;
        try
        {
            updated = ClarificationService.Answer(current, new(questionId, body.ExpectedRevision, body.Answer.Trim()));
        }
        catch (InvalidOperationException) { return Conflict(); }
        if (!await store.TryAnswerAsync(projectId, updated, body.ExpectedRevision, userId, cancellationToken)) return Conflict();
        // Answering is not an approval: the requirement review record is unchanged.
        return Ok(updated);
    }
}
