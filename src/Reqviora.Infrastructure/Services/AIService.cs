using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class AIService : IAIService
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Configure an AI provider before requesting completions.");
}
