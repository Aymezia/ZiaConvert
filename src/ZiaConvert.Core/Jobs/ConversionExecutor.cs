using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
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
    private readonly ConversionRouter _router;
    private readonly ILogger _logger;

    public ConversionExecutor(ConversionRouter router, ILogger<ConversionExecutor>? logger = null)
    {
        _router = router;
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

            return ConversionResult.Ok(request.OutputPath, engineName, stopwatch.Elapsed, detail);
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
}
