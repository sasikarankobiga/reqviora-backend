namespace Reqviora.Application.Features.Validation;

public enum ValidationWorkflow { Blueprint, Builder }

public sealed record ValidationSignal(string Code, string Path);

public sealed record ValidationErrorCounts(int Schema, int BrokenReferences, int UnsupportedComponents,
    int Navigation, int Other);

public sealed record ValidationStatistics(int Attempts, int Succeeded, int Failed, double SuccessRate,
    double FailureRate, double AverageDurationMilliseconds, ValidationErrorCounts Errors);

public sealed record ValidationDay(DateOnly Date, ValidationStatistics Statistics);

public interface IValidationHealth
{
    Task RecordAsync(ValidationWorkflow workflow, TimeSpan duration, IReadOnlyList<ValidationSignal> findings,
        CancellationToken cancellationToken);
    Task<ValidationStatistics> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
    Task<IReadOnlyList<ValidationDay>> DailyAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
