using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Tests.Jobs;

/// <summary>
/// Moteur de substitution : permet d'eprouver la file d'attente sans dependre d'un
/// binaire externe, et de mesurer le parallelisme reellement obtenu.
/// </summary>
internal sealed class FakeEngine : IConversionEngine
{
    private readonly Lock _gate = new();
    private readonly Dictionary<FormatFamily, int> _running = [];
    private readonly Dictionary<FormatFamily, int> _peak = [];

    public string Name => "fake";

    public IReadOnlySet<FormatFamily> SupportedFamilies { get; } =
        new HashSet<FormatFamily>(Enum.GetValues<FormatFamily>());

    /// <summary>Duree simulee de chaque conversion.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Fait echouer toute conversion dont l'entree contient ce fragment.</summary>
    public string? FailOnPathContaining { get; set; }

    public int StartedCount { get; private set; }

    /// <summary>Nombre maximal de conversions vues simultanement pour une famille.</summary>
    public int PeakConcurrency(FormatFamily family)
    {
        lock (_gate)
        {
            return _peak.GetValueOrDefault(family);
        }
    }

    public EngineAvailability CheckAvailability() => EngineAvailability.Available("fake");

    public bool CanHandle(ConversionRequest request) => true;

    public async IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var family = request.TargetFormat.Family;
        Enter(family);

        try
        {
            if (FailOnPathContaining is { } marker && request.InputPath.Contains(marker, StringComparison.Ordinal))
            {
                throw new ConversionException($"Echec simule pour {Path.GetFileName(request.InputPath)}.");
            }

            yield return ConversionProgress.Indeterminate(ConversionStage.Running, "Conversion simulee");

            var steps = 4;

            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(Duration / steps, cancellationToken).ConfigureAwait(false);
                yield return ConversionProgress.At(i * 100d / steps);
            }

            await File.WriteAllTextAsync(request.OutputPath, "sortie simulee", cancellationToken).ConfigureAwait(false);
            yield return ConversionProgress.Done();
        }
        finally
        {
            Leave(family);
        }
    }

    private void Enter(FormatFamily family)
    {
        lock (_gate)
        {
            StartedCount++;
            var current = _running.GetValueOrDefault(family) + 1;
            _running[family] = current;

            if (current > _peak.GetValueOrDefault(family))
            {
                _peak[family] = current;
            }
        }
    }

    private void Leave(FormatFamily family)
    {
        lock (_gate)
        {
            _running[family] = _running.GetValueOrDefault(family) - 1;
        }
    }
}
