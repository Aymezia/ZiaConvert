using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.Image;

/// <summary>
/// Moteur image, images classiques et negatifs RAW confondus.
/// </summary>
/// <remarks>
/// ImageMagick lit les RAW directement (libraw compile en dur, confirme par
/// <c>magick -list format</c>) : aucun outil separe n'est necessaire pour le dematricage.
/// </remarks>
public sealed class MagickEngine : IConversionEngine
{
    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly MagickArgsBuilder _arguments = new();
    private readonly ILogger _logger;

    public MagickEngine(IProcessRunner runner, IEngineLocator locator, ILogger<MagickEngine>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _logger = logger ?? NullLogger<MagickEngine>.Instance;
    }

    public string Name => "imagemagick";

    public IReadOnlySet<FormatFamily> SupportedFamilies { get; } =
        new HashSet<FormatFamily> { FormatFamily.Image, FormatFamily.RawImage };

    public EngineAvailability CheckAvailability() =>
        _locator.Locate("magick") is null
            ? EngineAvailability.Missing("ImageMagick (magick.exe) est introuvable.")
            : EngineAvailability.Available();

    public bool CanHandle(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Une demande d'agrandissement (UpscaleOptions) porte sur les memes formats
        // qu'une conversion ordinaire : c'est le type d'options, pas le couple de
        // formats, qui distingue les deux et doit orienter vers RealEsrganEngine.
        return request.Options is not UpscaleOptions
            && request.SourceFormat.Family is FormatFamily.Image or FormatFamily.RawImage
            && request.TargetFormat.Family == FormatFamily.Image;
    }

    public async IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var magick = _locator.Locate("magick")
            ?? throw new ConversionException("ImageMagick est introuvable : le moteur image n'est pas installe.");

        PrepareOutput(request);

        yield return ConversionProgress.Indeterminate(ConversionStage.Running, "Conversion de l'image");

        var arguments = _arguments.Build(request);
        _logger.LogDebug("magick {Arguments}", string.Join(' ', arguments));

        var processRequest = new ProcessRequest { FileName = magick, Arguments = arguments };

        try
        {
            // ImageMagick ne rapporte pas d'avancement en continu comme ffmpeg : une image
            // se convertit en une fraction de seconde a quelques secondes, un indicateur
            // indetermine suffit tant que la commande n'a pas rendu la main.
            await foreach (var line in _runner.StreamAsync(processRequest, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("magick: {Line}", line.Text);
            }
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

        if (!File.Exists(request.WorkingPath))
        {
            throw new ConversionException("magick s'est termine sans erreur mais n'a produit aucun fichier.");
        }

        File.Move(request.WorkingPath, request.OutputPath, overwrite: true);

        yield return ConversionProgress.Done();
    }

    private static void PrepareOutput(ConversionRequest request)
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

        DiscardPartialOutput(request);
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
            // Verrou transitoire apres la fin du processus : sans consequence, le prochain
            // essai ecrasera ce fichier.
        }
    }

    private static string DescribeFailure(ConversionRequest request, ProcessExecutionException failure)
    {
        var output = string.Join(' ', failure.ErrorTail);
        var file = Path.GetFileName(request.InputPath);

        if (output.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("improper image header", StringComparison.OrdinalIgnoreCase))
        {
            return $"« {file} » est illisible : le fichier est probablement endommage ou dans un format non reconnu.";
        }

        if (output.Contains("cache resources exhausted", StringComparison.OrdinalIgnoreCase))
        {
            return $"« {file} » est trop volumineux pour la memoire disponible.";
        }

        if (output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return $"Acces refuse a « {Path.GetFileName(request.OutputPath)} ».";
        }

        return $"La conversion de « {file} » a echoue.";
    }
}
