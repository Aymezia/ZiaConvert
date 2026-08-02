using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Tools;

namespace ZiaConvert.Engines.Upscale;

/// <summary>
/// Mesure combien de temps <c>realesrgan-ncnn-vulkan</c> met reellement a travailler sur
/// cette machine, pour pouvoir annoncer une duree avant de lancer un agrandissement.
/// </summary>
/// <remarks>
/// Impossible de deviner ce chiffre : il depend du GPU, du pilote, et meme de l'etat du
/// cache de shaders Vulkan (le premier lancement d'un modele donne est mesurablement
/// plus lent que les suivants — verifie : 4,7 s puis 2,8 s pour la meme image). La seule
/// reponse fiable est de chronometrer deux agrandissements de tailles connues et d'en
/// deduire un modele lineaire, une fois par (modele, facteur), mis en cache sur disque.
/// </remarks>
public sealed class UpscaleBenchmark
{
    // Cotes des images de calibration embarquees. La grande doit rester assez petite pour
    // que la calibration ne devienne pas elle-meme une attente genante (quelques secondes).
    private const int SmallSide = 128;
    private const int LargeSide = 768;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _cachePath;

    private UpscaleBenchmarkCache? _cache;

    public UpscaleBenchmark(IProcessRunner runner, IEngineLocator locator, ILogger<UpscaleBenchmark>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _logger = logger ?? NullLogger<UpscaleBenchmark>.Instance;
        _cachePath = Path.Combine(
            Path.GetDirectoryName(ToolLocator.UserEnginesDirectory) ?? Path.GetTempPath(),
            "upscale-benchmark.json");
    }

    /// <summary>
    /// Estime la duree d'un agrandissement, en calibrant au besoin.
    /// </summary>
    /// <param name="sourceWidth">Largeur de l'image source, avant agrandissement.</param>
    /// <param name="sourceHeight">Hauteur de l'image source, avant agrandissement.</param>
    /// <returns><c>null</c> si l'outil est introuvable : aucune estimation n'est alors possible.</returns>
    public async Task<TimeSpan?> EstimateAsync(
        int sourceWidth,
        int sourceHeight,
        UpscaleOptions options,
        CancellationToken cancellationToken = default)
    {
        var calibration = await GetCalibrationAsync(options, cancellationToken).ConfigureAwait(false);

        if (calibration is null)
        {
            return null;
        }

        var outputPixels = (long)sourceWidth * options.Factor * sourceHeight * options.Factor;

        return calibration.Estimate(outputPixels);
    }

    private async Task<UpscaleCalibration?> GetCalibrationAsync(UpscaleOptions options, CancellationToken cancellationToken)
    {
        var tool = _locator.Locate("realesrgan-ncnn-vulkan");

        if (tool is null)
        {
            return null;
        }

        var key = CalibrationKey(options);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var signature = BuildSignature(tool);
            var cache = _cache ??= ReadCache();

            if (cache is not null && cache.Signature == signature && cache.Calibrations.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (cache is null || cache.Signature != signature)
            {
                // Un realesrgan mis a jour invalide toutes les mesures precedentes, pas
                // seulement celle du couple demande.
                cache = new UpscaleBenchmarkCache { Signature = signature };
            }

            var measured = await MeasureAsync(tool, options, cancellationToken).ConfigureAwait(false);

            if (measured is null)
            {
                return null;
            }

            cache.Calibrations[key] = measured;
            _cache = cache;
            WriteCache(cache);

            return measured;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Chronometre deux agrandissements de taille connue et en deduit le cout fixe et
    /// le cout par megapixel par regression sur deux points.
    /// </summary>
    private async Task<UpscaleCalibration?> MeasureAsync(
        string tool,
        UpscaleOptions options,
        CancellationToken cancellationToken)
    {
        var workDirectory = Directory.CreateTempSubdirectory("ziaconvert-upscale-benchmark-").FullName;

        try
        {
            var smallInput = ExtractCalibrationImage("calibration-small.png", workDirectory, "small.png");
            var largeInput = ExtractCalibrationImage("calibration-large.png", workDirectory, "large.png");

            var smallElapsed = await RunTimedAsync(tool, smallInput, workDirectory, options, cancellationToken)
                .ConfigureAwait(false);
            var largeElapsed = await RunTimedAsync(tool, largeInput, workDirectory, options, cancellationToken)
                .ConfigureAwait(false);

            if (smallElapsed is null || largeElapsed is null)
            {
                _logger.LogWarning("Calibration Real-ESRGAN impossible : l'outil a echoue sur l'image de test.");
                return null;
            }

            var smallPixels = MegapixelsOf(SmallSide, options.Factor);
            var largePixels = MegapixelsOf(LargeSide, options.Factor);

            // Deux points (megapixels, secondes) determinent entierement une droite :
            // pente = cout par megapixel, ordonnee a l'origine = cout fixe.
            var slope = (largeElapsed.Value.TotalSeconds - smallElapsed.Value.TotalSeconds) / (largePixels - smallPixels);
            var fixedCost = smallElapsed.Value.TotalSeconds - (slope * smallPixels);

            var calibration = new UpscaleCalibration
            {
                // Un bruit de mesure peut rendre la pente legerement negative sur un GPU
                // tres rapide ou les deux tailles sont dominees par le cout fixe : la
                // duree ne peut alors que rester constante, jamais diminuer avec la taille.
                SecondsPerMegapixel = Math.Max(slope, 0d),
                FixedOverheadSeconds = Math.Max(fixedCost, 0d),
            };

            _logger.LogInformation(
                "Calibration Real-ESRGAN ({Model}, x{Factor}) : {Fixed:0.00} s fixe + {Rate:0.000} s/Mpx",
                options.Model, options.Factor, calibration.FixedOverheadSeconds, calibration.SecondsPerMegapixel);

            return calibration;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Sans consequence : un fichier de calibration oublie dans %TEMP%.
            }
        }
    }

    private async Task<TimeSpan?> RunTimedAsync(
        string tool,
        string inputPath,
        string workDirectory,
        UpscaleOptions options,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(inputPath) + "-out.png");
        var arguments = RealEsrganArgsBuilder.Build(inputPath, outputPath, "png", options, verbose: false);

        var stopwatch = Stopwatch.StartNew();

        var result = await _runner
            .RunAsync(new ProcessRequest { FileName = tool, Arguments = arguments }, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        if (!result.Success)
        {
            _logger.LogDebug("Calibration Real-ESRGAN echouee : {Error}", result.StandardErrorText);
            return null;
        }

        return stopwatch.Elapsed;
    }

    private static double MegapixelsOf(int side, int factor) => side * (long)factor * side * factor / 1_000_000d;

    private static string ExtractCalibrationImage(string resourceFileName, string directory, string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceFileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Ressource embarquee introuvable : {resourceFileName}.");

        var path = Path.Combine(directory, fileName);

        using (var file = File.Create(path))
        {
            stream.CopyTo(file);
        }

        return path;
    }

    private static string CalibrationKey(UpscaleOptions options) =>
        string.Create(CultureInfo.InvariantCulture, $"{options.Model}|{options.Factor}");

    /// <summary>Identifie le binaire par son chemin, sa taille et sa date : le mettre a jour invalide le cache.</summary>
    private static string? BuildSignature(string tool)
    {
        if (!File.Exists(tool))
        {
            return null;
        }

        var info = new FileInfo(tool);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
    }

    private UpscaleBenchmarkCache? ReadCache()
    {
        try
        {
            return File.Exists(_cachePath)
                ? JsonSerializer.Deserialize<UpscaleBenchmarkCache>(File.ReadAllText(_cachePath), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cache de calibration Real-ESRGAN illisible, nouvelle mesure.");
            return null;
        }
    }

    private void WriteCache(UpscaleBenchmarkCache cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Impossible d'ecrire le cache de calibration Real-ESRGAN.");
        }
    }
}
