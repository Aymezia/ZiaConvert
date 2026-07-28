using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Jobs;

public enum JobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Une conversion suivie de bout en bout : son etat, son avancement et son issue.
/// </summary>
/// <remarks>
/// L'objet est mute par le worker qui l'execute et lu par l'interface. Les champs exposes
/// sont volatils ou proteges, et <see cref="Changed" /> est leve a chaque evolution afin
/// que l'interface n'ait pas a interroger l'objet en boucle.
/// </remarks>
public sealed class ConversionJob
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _gate = new();

    private JobState _state = JobState.Queued;
    private ConversionProgress? _progress;
    private ConversionResult? _result;

    public ConversionJob(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    /// <summary>Leve a chaque changement d'etat ou d'avancement.</summary>
    public event EventHandler<ConversionJob>? Changed;

    public Guid Id { get; } = Guid.NewGuid();

    public ConversionRequest Request { get; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public JobState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public ConversionProgress? Progress
    {
        get
        {
            lock (_gate)
            {
                return _progress;
            }
        }
    }

    public ConversionResult? Result
    {
        get
        {
            lock (_gate)
            {
                return _result;
            }
        }
    }

    public bool IsFinished => State is JobState.Completed or JobState.Failed or JobState.Cancelled;

    internal CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Demande l'arret. Sans effet si la conversion est deja terminee.</summary>
    public void Cancel()
    {
        if (IsFinished)
        {
            return;
        }

        _cancellation.Cancel();
    }

    internal void MarkRunning()
    {
        lock (_gate)
        {
            _state = JobState.Running;
            StartedAt = DateTimeOffset.Now;
        }

        RaiseChanged();
    }

    internal void Report(ConversionProgress progress)
    {
        lock (_gate)
        {
            _progress = progress;
        }

        RaiseChanged();
    }

    internal void Complete(ConversionResult result)
    {
        lock (_gate)
        {
            _result = result;
            _state = result.Success ? JobState.Completed : JobState.Failed;
            _progress = result.Success ? ConversionProgress.Done() : _progress;
            FinishedAt = DateTimeOffset.Now;
        }

        RaiseChanged();
    }

    internal void MarkCancelled()
    {
        lock (_gate)
        {
            _state = JobState.Cancelled;
            FinishedAt = DateTimeOffset.Now;
        }

        RaiseChanged();
    }

    internal void Dispose() => _cancellation.Dispose();

    private void RaiseChanged() => Changed?.Invoke(this, this);
}
