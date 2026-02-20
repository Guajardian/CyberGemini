using System.Threading;

namespace CyberGemini.Utilities;

public sealed class PauseTokenSource
{
    private readonly ManualResetEventSlim _pauseEvent = new(true);

    public bool IsPaused => !_pauseEvent.IsSet;

    public PauseToken Token => new(this);

    public void Pause() => _pauseEvent.Reset();

    public void Resume() => _pauseEvent.Set();

    internal void WaitWhilePaused(CancellationToken token) => _pauseEvent.Wait(token);
}

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source)
    {
        _source = source;
    }

    public bool IsPaused => _source?.IsPaused ?? false;

    public void WaitWhilePaused(CancellationToken token) => _source?.WaitWhilePaused(token);
}