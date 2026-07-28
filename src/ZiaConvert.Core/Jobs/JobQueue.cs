using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Jobs;

/// <summary>
/// File d'attente des conversions, servie par un nombre borne de workers.
/// </summary>
/// <remarks>
/// Le bornage est la raison d'etre de cette classe. Lacher une conversion par fichier
/// depose ferait s'effondrer la machine des la dixieme video : chaque encodage utilise
/// deja tous les cœurs disponibles. Les limites sont definies par famille dans
/// <see cref="ConcurrencyPolicy" />.
/// </remarks>
public sealed class JobQueue : IAsyncDisposable
{
    private readonly Channel<ConversionJob> _channel = Channel.CreateUnbounded<ConversionJob>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly ConcurrentDictionary<Guid, ConversionJob> _jobs = new();
    private readonly Dictionary<FormatFamily, SemaphoreSlim> _gates = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConversionExecutor _executor;
    private readonly ConcurrencyPolicy _policy;
    private readonly ILogger _logger;
    private readonly Task[] _workers;

    public JobQueue(
        ConversionExecutor executor,
        ConcurrencyPolicy? policy = null,
        ILogger<JobQueue>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(executor);

        _executor = executor;
        _policy = policy ?? ConcurrencyPolicy.Default;
        _logger = logger ?? NullLogger<JobQueue>.Instance;

        foreach (var family in Enum.GetValues<FormatFamily>())
        {
            _gates[family] = new SemaphoreSlim(_policy.For(family));
        }

        _workers = new Task[_policy.MaxWorkers];

        for (var i = 0; i < _workers.Length; i++)
        {
            _workers[i] = Task.Run(() => WorkAsync(_shutdown.Token));
        }
    }

    /// <summary>Leve a chaque evolution d'un job, quel qu'il soit.</summary>
    public event EventHandler<ConversionJob>? JobChanged;

    public IReadOnlyCollection<ConversionJob> Jobs => _jobs.Values.ToList();

    public int PendingCount => _jobs.Values.Count(j => !j.IsFinished);

    public ConversionJob Enqueue(ConversionRequest request)
    {
        var job = new ConversionJob(request);

        job.Changed += OnJobChanged;
        _jobs[job.Id] = job;

        if (!_channel.Writer.TryWrite(job))
        {
            throw new InvalidOperationException("La file de conversion est fermee.");
        }

        return job;
    }

    /// <summary>Demande l'arret de tous les jobs en attente ou en cours.</summary>
    public void CancelAll()
    {
        foreach (var job in _jobs.Values.Where(j => !j.IsFinished))
        {
            job.Cancel();
        }
    }

    /// <summary>Attend que la file soit vide.</summary>
    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (PendingCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Arret demande : c'est le deroulement attendu.
        }

        foreach (var job in _jobs.Values)
        {
            job.Changed -= OnJobChanged;
            job.Dispose();
        }

        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _shutdown.Dispose();
    }

    private async Task WorkAsync(CancellationToken shutdownToken)
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(shutdownToken).ConfigureAwait(false))
            {
                await RunJobAsync(job, shutdownToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Extinction de la file.
        }
    }

    private async Task RunJobAsync(ConversionJob job, CancellationToken shutdownToken)
    {
        var gate = _gates[job.Request.TargetFormat.Family];

        // L'annulation d'un job en attente doit etre immediate : inutile de lui faire
        // patienter derriere le verrou de famille pour le rejeter juste apres.
        if (job.CancellationToken.IsCancellationRequested)
        {
            job.MarkCancelled();
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken, shutdownToken);

        try
        {
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.MarkCancelled();
            return;
        }

        try
        {
            job.MarkRunning();

            var result = await _executor
                .ExecuteAsync(job.Request, new Progress<ConversionProgress>(job.Report), linked.Token)
                .ConfigureAwait(false);

            job.Complete(result);
        }
        catch (OperationCanceledException)
        {
            job.MarkCancelled();
        }
#pragma warning disable CA1031 // Un worker ne doit jamais mourir : l'echec appartient au job.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Echec inattendu du job {JobId}.", job.Id);
            job.Complete(ConversionResult.Failed(job.Request.OutputPath, "inconnu", ex.Message));
        }
#pragma warning restore CA1031
        finally
        {
            gate.Release();
        }
    }

    private void OnJobChanged(object? sender, ConversionJob job) => JobChanged?.Invoke(this, job);
}
