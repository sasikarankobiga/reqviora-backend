namespace Reqviora.Application.Features.RequirementReview;

public static class ProjectReviewLock
{
    public static long PostgreSqlKey(Guid projectId) =>
        BitConverter.ToInt64(projectId.ToByteArray(), 0);

    public static string SqlServerResource(Guid projectId) =>
        $"reqviora:requirement-review:{projectId:N}";
}
