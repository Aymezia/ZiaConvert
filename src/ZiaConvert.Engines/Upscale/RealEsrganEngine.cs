using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.Upscale;

/// <summary>
/// Agrandissement d'image par reseau de neurones (Real-ESRGAN, execution ncnn/Vulkan).
/// </summary>
/// <remarks>
/// A ne pas confondre avec un simple redimensionnement : celui-ci reconstruit du detail
/// plausible plutot que d'etirer les pixels existants, au prix de plusieurs secondes par
/// image. Reserve aux images deja developpees (RAW exclu : l'outil ne lit que jpg/png/webp,
/// confirme par son propre message d'aide) et declenche uniquement quand la demande porte
/// des <see cref="UpscaleOptions" /> — c'est ce choix explicite qui le distingue d'une
/// conversion ordinaire vers le meme format, que <see cref="Image.MagickEngine" /> saurait
/// tout aussi bien satisfaire.
/// </remarks>
public sealed class RealEsrganEngine : IConversionEngine
{
    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly UpscaleBenchmark _benchmark;
    private readonly ILogger _logger;

    public RealEsrganEngine(
        IProcessRunner runner,
        IEngineLocator locator,
        UpscaleBenchmark benchmark,
        ILogger<RealEsrganEngine>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _benchmark = benchmark;
        _logger = logger ?? NullLogger<RealEsrganEngine>.Instance;
    }

    public string Name => "realesrgan";

    public IReadOnlySet<FormatFamily> SupportedFamilies { get; } = new HashSet<FormatFamily> { FormatFamily.Image };

    public EngineAvailability CheckAvailability() =>
        _locator.Locate("realesrgan-ncnn-vulkan") is null
            ? EngineAvailability.Missing("Real-ESRGAN (realesrgan-ncnn-vulkan) est introuvable.")
            : EngineAvailability.Available();

    public bool CanHandle(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Options is UpscaleOptions
            && request.SourceFormat.Family == FormatFamily.Image
            && request.TargetFormat.Family == FormatFamily.Image;
    }

    /// <summary>Estime la duree avant meme de lancer la conversion, pour l'afficher a l'utilisateur.</summary>
    public Task<TimeSpan?> EstimateDurationAsync(
        int sourceWidth,
        int sourceHeight,
        UpscaleOptions options,
        CancellationToken cancellationToken = default) =>
        _benchmark.EstimateAsync(sourceWidth, sourceHeight, options, cancellationToken);

    public async IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tool = _locator.Locate("realesrgan-ncnn-vulkan")
            ?? throw new ConversionException("Real-ESRGAN est introuvable : le moteur d'agrandissement n'est pas installe.");

        var options = request.Options as UpscaleOptions ?? new UpscaleOptions();

        // L'outil valide lui-meme l'extension du chemin de sortie et refuse tout ce qui
        // n'est pas jpg/png/webp (verifie : « invalid outputpath extension type ») : le
        // fichier de travail « .part » habituel, partage par les autres moteurs, ne
        // convient donc pas ici. On garde neanmoins la meme garantie d'ecriture atomique
        // avec un nom temporaire different, a extension valide.
        var workingPath = BuildWorkingPath(request, out var format);

        PrepareOutput(request, workingPath);

        yield return ConversionProgress.Indeterminate(ConversionStage.Running, "Agrandissement de l'image");

        var arguments = RealEsrganArgsBuilder.Build(request.InputPath, workingPath, format, options, verbose: true);
        _logger.LogDebug("realesrgan-ncnn-vulkan {Arguments}", string.Join(' ', arguments));

        var processRequest = new ProcessRequest { FileName = tool, Arguments = arguments };

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
                DiscardPartialOutput(workingPath);
                throw new ConversionException(DescribeFailure(request, ex), Name, ex.ErrorTail);
            }
            catch (OperationCanceledException)
            {
                DiscardPartialOutput(workingPath);
                throw;
            }

            if (line.IsError)
            {
                // Le premier lancement d'un modele imprime la fiche du GPU sur la
                // sortie d'erreur (nom, capacites Vulkan) : utile en diagnostic, pas
                // une erreur en soi.
                _logger.LogDebug("realesrgan: {Line}", line.Text);
                continue;
            }

            // Verifie empiriquement (image de 1600x1200, 48 tuiles) : l'outil imprime un
            // seul compteur continu de 0 a 100 sur l'ensemble du travail, pas un
            // pourcentage remis a zero a chaque tuile. Le transmettre tel quel suffit.
            if (RealEsrganProgressParser.TryParse(line.Text) is { } percent)
            {
                yield return ConversionProgress.At(percent);
            }
        }

        if (!File.Exists(workingPath))
        {
            throw new ConversionException("realesrgan-ncnn-vulkan s'est termine sans erreur mais n'a produit aucun fichier.");
        }

        File.Move(workingPath, request.OutputPath, overwrite: true);

        yield return ConversionProgress.Done();
    }

    /// <summary>
    /// Choisit un nom de travail a extension valide dans le meme dossier que la sortie
    /// finale, et determine au passage le format a demander via <c>-f</c>.
    /// </summary>
    private static string BuildWorkingPath(ConversionRequest request, out string format)
    {
        format = request.TargetFormat.Id switch
        {
            "jpeg" => "jpg",
            "png" or "webp" => request.TargetFormat.Id,

            // Format non supporte directement par l'outil (avif, heic, tiff...) : on
            // produit un PNG intermediaire, sans perte, que MagickEngine pourrait ensuite
            // reconvertir si necessaire. Simple et sur, au prix d'une etape en plus pour
            // ces formats moins courants en sortie d'agrandissement.
            _ => "png",
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath)) ?? ".";

        return Path.Combine(directory, $".{Guid.NewGuid():N}.{format}");
    }

    private static void PrepareOutput(ConversionRequest request, string workingPath)
    {
        if (!request.Overwrite && File.Exists(request.OutputPath))
        {
            throw new ConversionException($"« {Path.GetFileName(request.OutputPath)} » existe deja.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        DiscardPartialOutput(workingPath);
    }

    private static void DiscardPartialOutput(string workingPath)
    {
        try
        {
            if (File.Exists(workingPath))
            {
                File.Delete(workingPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Verrou transitoire apres la fin du processus : sans consequence.
        }
    }

    private static string DescribeFailure(ConversionRequest request, ProcessExecutionException failure)
    {
        var output = string.Join(' ', failure.ErrorTail);
        var file = Path.GetFileName(request.InputPath);

        if (output.Contains("decode image failed", StringComparison.OrdinalIgnoreCase))
        {
            return $"« {file} » est illisible par Real-ESRGAN : seuls jpg, png et webp sont pris en charge.";
        }

        if (output.Contains("vkAllocateMemory", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("out of device memory", StringComparison.OrdinalIgnoreCase))
        {
            return "Memoire GPU insuffisante pour cette image. Reduire la taille des tuiles peut resoudre le probleme.";
        }

        return $"L'agrandissement de « {file} » a echoue.";
    }
}
