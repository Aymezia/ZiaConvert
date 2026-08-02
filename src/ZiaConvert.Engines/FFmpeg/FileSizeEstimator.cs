using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>Estimation de la taille finale d'une conversion video.</summary>
/// <param name="EstimatedBytes">Taille de sortie estimee, en octets.</param>
/// <param name="IsSampled">
/// Vrai quand l'estimation vient d'un encodage d'echantillon extrapole (CRF, debit
/// impose) ; faux quand c'est une mesure directe (remux : la sortie fait quasiment la
/// taille de la source, aucun echantillon n'est necessaire).
/// </param>
public sealed record FileSizeEstimate(long EstimatedBytes, bool IsSampled);

/// <summary>
/// Estime la taille de sortie d'une conversion video avant de la lancer.
/// </summary>
/// <remarks>
/// Pas de formule : a qualite constante (CRF), la taille resultante depend du contenu
/// (mouvement, detail, bruit) et aucun calcul ne la devine correctement. La seule reponse
/// fiable est d'encoder un veritable extrait avec les memes reglages et d'extrapoler —
/// meme principe que <see cref="Upscale.UpscaleBenchmark" /> pour la duree d'agrandissement :
/// mesurer plutot que supposer. L'extrait est pris a 25% du fichier plutot qu'au debut,
/// pour eviter un generique ou un ecran noir non representatif du contenu reel.
/// </remarks>
public sealed class FileSizeEstimator
{
    private const double SampleDurationSeconds = 8d;
    private const double SamplePositionFraction = 0.25;

    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly HardwareDetector _hardware;
    private readonly FFmpegArgsBuilder _argsBuilder = new();
    private readonly ILogger _logger;

    public FileSizeEstimator(
        IProcessRunner runner,
        IEngineLocator locator,
        HardwareDetector hardware,
        ILogger<FileSizeEstimator>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _hardware = hardware;
        _logger = logger ?? NullLogger<FileSizeEstimator>.Instance;
    }

    /// <returns><c>null</c> quand l'estimation n'est pas possible (pas ffmpeg, duree source inconnue...).</returns>
    public async Task<FileSizeEstimate?> EstimateAsync(
        ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetFormat.Family != FormatFamily.Video ||
            request.SourceInfo?.Duration is not { } totalDuration ||
            totalDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var ffmpeg = _locator.Locate("ffmpeg");

        if (ffmpeg is null)
        {
            return null;
        }

        var hardware = await _hardware.DetectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        FFmpegPlan plan;

        try
        {
            plan = _argsBuilder.Build(request, hardware);
        }
        catch (UnsupportedConversionException)
        {
            // Le veritable essai de conversion produira la meme erreur, avec un message
            // deja pense pour l'utilisateur : inutile de le dupliquer ici.
            return null;
        }

        if (plan.IsRemux)
        {
            // Une copie de flux ne change pratiquement pas la taille : le conteneur differe
            // de quelques kilo-octets d'en-tetes, pas plus. Mesure directe, pas d'echantillon.
            return File.Exists(request.InputPath)
                ? new FileSizeEstimate(new FileInfo(request.InputPath).Length, IsSampled: false)
                : null;
        }

        return await SampleAsync(ffmpeg, request, hardware, totalDuration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FileSizeEstimate?> SampleAsync(
        string ffmpeg,
        ConversionRequest request,
        HardwareSupport hardware,
        TimeSpan totalDuration,
        CancellationToken cancellationToken)
    {
        var sampleDuration = TimeSpan.FromSeconds(Math.Min(SampleDurationSeconds, totalDuration.TotalSeconds));

        var latestStart = totalDuration - sampleDuration;
        var sampleStart = TimeSpan.FromSeconds(totalDuration.TotalSeconds * SamplePositionFraction);

        if (sampleStart > latestStart)
        {
            sampleStart = latestStart > TimeSpan.Zero ? latestStart : TimeSpan.Zero;
        }

        var options = request.Options as VideoOptions ?? new VideoOptions();
        var sampleOptions = options with { StartTime = sampleStart, EndTime = sampleStart + sampleDuration };

        var workDirectory = Directory.CreateTempSubdirectory("ziaconvert-size-estimate-").FullName;

        try
        {
            var sampleRequest = request with
            {
                Options = sampleOptions,
                OutputPath = Path.Combine(workDirectory, "echantillon.tmp"),
            };

            var samplePlan = _argsBuilder.Build(sampleRequest, hardware);

            var result = await _runner
                .RunAsync(new ProcessRequest { FileName = ffmpeg, Arguments = samplePlan.Arguments }, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success || !File.Exists(sampleRequest.WorkingPath))
            {
                _logger.LogDebug("Echantillonnage de taille impossible : {Error}", result.StandardErrorText);
                return null;
            }

            var sampleBytes = new FileInfo(sampleRequest.WorkingPath).Length;
            var ratio = totalDuration.TotalSeconds / sampleDuration.TotalSeconds;

            return new FileSizeEstimate((long)(sampleBytes * ratio), IsSampled: true);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Sans consequence : un dossier oublie dans %TEMP%.
            }
        }
    }
}
