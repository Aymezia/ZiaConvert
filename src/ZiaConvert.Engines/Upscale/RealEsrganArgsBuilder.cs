using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.Upscale;

/// <summary>
/// Construit les arguments de <c>realesrgan-ncnn-vulkan</c>.
/// </summary>
/// <remarks>
/// Partagee entre <see cref="RealEsrganEngine" /> (conversion reelle) et
/// <see cref="UpscaleBenchmark" /> (mesure de calibration) : les deux doivent lancer
/// l'outil avec exactement les memes options pour que la duree mesuree reste
/// representative de la duree reelle.
/// </remarks>
internal static class RealEsrganArgsBuilder
{
    /// <summary>Nom de format accepte par <c>-f</c>, tel qu'imprime dans le message d'erreur observe.</summary>
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase) { "jpg", "png", "webp" };

    public static IReadOnlyList<string> Build(
        string inputPath,
        string outputPath,
        string outputFormat,
        UpscaleOptions options,
        bool verbose)
    {
        var format = NormalizeFormat(outputFormat);

        var builder = new ArgumentBuilder()
            .Add("-i", inputPath)
            .Add("-o", outputPath)

            // Sans -f explicite, l'outil deduit le format de l'extension du chemin de
            // sortie et rejette toute extension qu'il ne reconnait pas (verifie : « invalid
            // outputpath extension type » sur un chemin de travail nomme differemment).
            .Add("-f", format)
            .Add("-n", options.Model)
            .Add("-s", options.Factor)
            .Add("-t", options.TileSize)
            .AddIf(verbose, "-v");

        if (options.GpuId is { } gpuId)
        {
            builder.Add("-g", gpuId);
        }

        return builder.Build();
    }

    /// <summary>Le nom de fichier n'est pas garanti correspondre a un format accepte (RAW, tiff...) : on retombe sur PNG, toujours accepte.</summary>
    private static string NormalizeFormat(string requested) =>
        SupportedFormats.Contains(requested) ? requested.ToLowerInvariant() : "png";
}
