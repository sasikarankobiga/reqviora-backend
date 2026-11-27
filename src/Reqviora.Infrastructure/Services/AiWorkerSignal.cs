namespace Reqviora.Infrastructure.Services;

public sealed class AiWorkerSignal
{
    private Poll? _lastPoll;

    public Poll? LastPoll => Volatile.Read(ref _lastPoll);

    public void Record(DateTimeOffset at, bool succeeded) =>
        Volatile.Write(ref _lastPoll, new Poll(at, succeeded));

    public sealed record Poll(DateTimeOffset At, bool Succeeded);
}
