using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Moteur audio et video. Couvre les conversions entre conteneurs, le reencodage, le
/// redimensionnement, l'extraction de bande son et la fabrication de GIF.
/// </summary>
public sealed class FFmpegEngine : IConversionEngine
{
    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly IMediaProbe _probe;
    private readonly HardwareDetector _hardware;
    private readonly FFmpegArgsBuilder _arguments = new();
    private readonly ILogger _logger;

    public FFmpegEngine(
        IProcessRunner runner,
        IEngineLocator locator,
        IMediaProbe probe,
        HardwareDetector hardware,
        ILogger<FFmpegEngine>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _probe = probe;
        _hardware = hardware;
        _logger = logger ?? NullLogger<FFmpegEngine>.Instance;
    }

    public string Name => "ffmpeg";

    public IReadOnlySet<FormatFamily> SupportedFamilies { get; } =
        new HashSet<FormatFamily> { FormatFamily.Video, FormatFamily.Audio };

    public EngineAvailability CheckAvailability()
    {
        if (_locator.Locate("ffmpeg") is null)
        {
            return EngineAvailability.Missing("ffmpeg est introuvable.");
        }

        return _locator.Locate("ffprobe") is null
            ? EngineAvailability.Missing("ffprobe est introuvable : il accompagne normalement ffmpeg.")
            : EngineAvailability.Available();
    }

    public bool CanHandle(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (FFmpegMuxers.For(request.TargetFormat.Id) is null)
        {
            return false;
        }

        var source = request.SourceFormat.Family;

        return request.TargetFormat switch
        {
            // Le GIF est la seule image que ce moteur produit, et seulement depuis une video.
            { Id: "gif" } => source == FormatFamily.Video,
            { Family: FormatFamily.Audio } => source is FormatFamily.Video or FormatFamily.Audio,
            { Family: FormatFamily.Video } => source == FormatFamily.Video,
            _ => false,
        };
    }

    public async IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ffmpeg = _locator.Locate("ffmpeg")
            ?? throw new ConversionException("ffmpeg est introuvable : le moteur video n'est pas installe.");

        PrepareOutput(request);

        yield return ConversionProgress.Indeterminate(ConversionStage.Analyzing, "Analyse du fichier source");

        var source = request.SourceInfo ?? await _probe.ProbeAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        var hardware = await _hardware.DetectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var plan = _arguments.Build(request with { SourceInfo = source }, hardware);

        _logger.LogInformation("{Description} : {File}", plan.Description, Path.GetFileName(request.InputPath));
        _logger.LogDebug("ffmpeg {Arguments}", string.Join(' ', plan.Arguments));

        yield return ConversionProgress.Indeterminate(ConversionStage.Running, plan.Description);

        var processRequest = new ProcessRequest
        {
            FileName = ffmpeg,
            Arguments = plan.Arguments,

            // « q » demande a ffmpeg de finaliser proprement le conteneur avant de rendre
            // la main. Une terminaison brutale laisserait un fichier sans index, illisible.
            GracefulStopInput = "q",
            GracefulStopTimeout = TimeSpan.FromSeconds(5),
        };

        var parser = new FFmpegProgressParser();
        var stopwatch = Stopwatch.StartNew();

        await using var lines = _runner.StreamAsync(processRequest, cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ProcessOutputLine line;

            // La lecture est isolee dans un try afin de pouvoir nettoyer le fichier partiel :
            // un yield return ne peut pas cohabiter avec un catch dans la meme portee.
            try
            {
                if (!await lines.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                line = lines.Current;
            }
            catch (ProcessExecutionException ex)
            {
                DiscardPartialOutput(request);
                throw new ConversionException(DescribeFailure(request, ex), Name, ex.ErrorTail);
            }
            catch (OperationCanceledException)
            {
                DiscardPartialOutput(request);
                throw;
            }

            if (line.IsError)
            {
                _logger.LogDebug("ffmpeg: {Line}", line.Text);
                continue;
            }

            if (parser.Feed(line.Text) is { } snapshot && !snapshot.IsFinal)
            {
                yield return ToProgress(snapshot, source.Duration, stopwatch.Elapsed);
            }
        }

        yield return ConversionProgress.Indeterminate(ConversionStage.Finalizing, "Finalisation");

        Finalize(request);

        yield return ConversionProgress.Done();
    }

    /// <summary>
    /// Verifie la destination et efface les restes d'une tentative precedente.
    /// </summary>
    private static void PrepareOutput(ConversionRequest request)
    {
        if (!request.Overwrite && File.Exists(request.OutputPath))
        {
            throw new ConversionException(
                $"« {Path.GetFileName(request.OutputPath)} » existe deja.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        DiscardPartialOutput(request);
    }

    /// <summary>
    /// Publie le resultat sous son nom definitif. Tant que cette etape n'a pas eu lieu,
    /// aucun fichier utilisable ne porte le nom attendu : c'est ce qui garantit qu'une
    /// conversion interrompue ne laisse jamais de sortie a moitie ecrite.
    /// </summary>
    private static void Finalize(ConversionRequest request)
    {
        if (!File.Exists(request.WorkingPath))
        {
            throw new ConversionException(
                "ffmpeg s'est termine sans erreur mais n'a produit aucun fichier.");
        }

        File.Move(request.WorkingPath, request.OutputPath, overwrite: true);
    }

    private static void DiscardPartialOutput(ConversionRequest request)
    {
        try
        {
            if (File.Exists(request.WorkingPath))
            {
                File.Delete(request.WorkingPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le fichier temporaire peut rester verrouille un instant apres la terminaison
            // du processus. Ce n'est pas bloquant : il sera ecrase a la prochaine tentative.
        }
    }

    private static ConversionProgress ToProgress(
        FFmpegProgressSnapshot snapshot,
        TimeSpan? totalDuration,
        TimeSpan elapsed)
    {
        double? percent = null;
        TimeSpan? eta = null;

        if (totalDuration is { TotalSeconds: > 0 } total && snapshot.OutTime is { } position)
        {
            percent = Math.Clamp(position.TotalSeconds / total.TotalSeconds * 100d, 0d, 100d);

            // L'estimation par la vitesse est plus stable que par le pourcentage : elle ne
            // depend pas du temps deja ecoule, donc elle ne s'emballe pas au demarrage.
            if (snapshot.Speed is { } speed and > 0.01d)
            {
                var remaining = total - position;

                if (remaining > TimeSpan.Zero)
                {
                    eta = TimeSpan.FromSeconds(remaining.TotalSeconds / speed);
                }
            }
        }

        return new ConversionProgress
        {
            Percent = percent,
            Stage = ConversionStage.Running,
            Elapsed = elapsed,
            Eta = eta,
            Speed = snapshot.Speed,
            OutputBytes = snapshot.TotalSize,
        };
    }

    /// <summary>
    /// Traduit un echec ffmpeg en message comprehensible. La sortie brute reste attachee
    /// a l'exception pour le diagnostic, mais elle ne convient pas a un utilisateur.
    /// </summary>
    private static string DescribeFailure(ConversionRequest request, ProcessExecutionException failure)
    {
        var output = string.Join(' ', failure.ErrorTail);
        var file = Path.GetFileName(request.InputPath);

        if (output.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
        {
            return $"« {file} » est illisible : le fichier est probablement endommage.";
        }

        if (output.Contains("No space left", StringComparison.OrdinalIgnoreCase))
        {
            return "Espace disque insuffisant pour ecrire le fichier de sortie.";
        }

        if (output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return $"Acces refuse a « {Path.GetFileName(request.OutputPath)} ».";
        }

        if (output.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase))
        {
            return "L'encodeur demande n'est pas disponible dans cette version de ffmpeg.";
        }

        return $"La conversion de « {file} » a echoue.";
    }
}
