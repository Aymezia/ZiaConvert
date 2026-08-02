using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Routing;

namespace ZiaConvert.Core.Jobs;

/// <summary>
/// Deroule une conversion complete : analyse, choix du moteur, execution, mesure.
/// </summary>
/// <remarks>
/// Sert aussi bien a la ligne de commande, qui n'a qu'un fichier a traiter, qu'aux workers
/// de <see cref="JobQueue" />. Aucun echec ne s'echappe sous forme d'exception : tout
/// revient dans un <see cref="ConversionResult" />, sauf l'annulation qui est propagee.
/// </remarks>
public sealed class ConversionExecutor
{
    /// <summary>
    /// Ecart tolere entre la duree de la sortie et celle attendue, au dela du tolerable
    /// pour un simple arrondi de conteneur. Le plus grand des deux : quelques secondes
    /// fixes pour les extraits courts, un pourcentage pour les longs.
    /// </summary>
    private static readonly TimeSpan MinimumTolerance = TimeSpan.FromSeconds(2);
    private const double ToleranceFraction = 0.05;

    private readonly ConversionRouter _router;
    private readonly IMediaProbe? _probe;
    private readonly ILogger _logger;

    /// <param name="probe">
    /// Sert a re-sonder la sortie une fois la conversion terminee, pour detecter un
    /// fichier tronque malgre un moteur qui n'a rien signale. Optionnel : sans sonde,
    /// la verification est simplement ignoree plutot que de faire echouer l'appelant.
    /// </param>
    public ConversionExecutor(ConversionRouter router, IMediaProbe? probe = null, ILogger<ConversionExecutor>? logger = null)
    {
        _router = router;
        _probe = probe;
        _logger = logger ?? NullLogger<ConversionExecutor>.Instance;
    }

    /// <exception cref="OperationCanceledException">L'annulation a ete demandee.</exception>
    public async Task<ConversionResult> ExecuteAsync(
        ConversionRequest request,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var engineName = "inconnu";

        try
        {
            var prepared = await _router.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
            var engine = _router.SelectEngine(prepared);
            engineName = engine.Name;

            string? detail = null;

            await foreach (var step in engine.ExecuteAsync(prepared, cancellationToken).ConfigureAwait(false))
            {
                // Le moteur annonce en clair ce qu'il s'apprete a faire des qu'il le sait :
                // c'est cette phrase qu'on rend a l'appelant pour justifier la duree.
                detail ??= step is { Stage: ConversionStage.Running, Message: { Length: > 0 } message }
                    ? message
                    : null;

                progress?.Report(step);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "{Input} converti en {Duration:0.0} s ({Detail})",
                Path.GetFileName(request.InputPath),
                stopwatch.Elapsed.TotalSeconds,
                detail);

            var warning = await VerifyAsync(prepared, cancellationToken).ConfigureAwait(false);

            if (warning is not null)
            {
                _logger.LogWarning("{Output} : {Warning}", Path.GetFileName(request.OutputPath), warning);
            }

            return ConversionResult.Ok(request.OutputPath, engineName, stopwatch.Elapsed, detail, warning);
        }
        catch (ConversionException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Echec de la conversion de {Input}.", request.InputPath);

            return ConversionResult.Failed(request.OutputPath, engineName, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Acces au disque impossible pour {Input}.", request.InputPath);

            return ConversionResult.Failed(request.OutputPath, engineName, ex.Message);
        }
    }

    /// <summary>
    /// Re-sonde la sortie et compare sa duree a celle de la source, pour detecter un
    /// fichier tronque malgre un moteur qui n'a rien signale d'anormal.
    /// </summary>
    /// <remarks>
    /// N'est jamais laisse en position de transformer une conversion reussie en echec :
    /// toute exception ici est avalee, la verification est par nature secondaire par
    /// rapport au resultat principal deja acquis.
    /// </remarks>
    private async Task<string?> VerifyAsync(ConversionRequest request, CancellationToken cancellationToken)
    {
        if (_probe is null || request.TargetFormat.Family is not (FormatFamily.Video or FormatFamily.Audio))
        {
            return null;
        }

        // Sans duree source connue, ou avec un extrait demande, il n'y a pas de reference
        // fiable a comparer : mieux vaut rester muet qu'un faux positif.
        if (request.SourceInfo?.Duration is not { } expectedDuration || HasTrim(request.Options))
        {
            return null;
        }

        try
        {
            var outputInfo = await _probe.ProbeAsync(request.OutputPath, cancellationToken).ConfigureAwait(false);

            if (outputInfo.Duration is not { } actualDuration)
            {
                return null;
            }

            var tolerance = MinimumTolerance > expectedDuration * ToleranceFraction
                ? MinimumTolerance
                : expectedDuration * ToleranceFraction;

            var difference = (expectedDuration - actualDuration).Duration();

            return difference > tolerance
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"La sortie dure {actualDuration.TotalSeconds:0.0} s, la source en durait {expectedDuration.TotalSeconds:0.0} s — le fichier semble tronque.")
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // La sortie existe (le moteur a reussi) mais la sonde echoue a la relire :
            // signal fort en soi, pas une raison de rester muet.
            _logger.LogDebug(ex, "Verification impossible pour {Output}.", request.OutputPath);

            return "Le fichier produit n'a pas pu etre relu pour verification : il est peut-etre corrompu.";
        }
    }

    private static bool HasTrim(ConversionOptions options) => options switch
    {
        VideoOptions v => v.StartTime is not null || v.EndTime is not null,
        AudioOptions a => a.StartTime is not null || a.EndTime is not null,
        GifOptions g => g.StartTime is not null || g.EndTime is not null,
        _ => false,
    };
}
