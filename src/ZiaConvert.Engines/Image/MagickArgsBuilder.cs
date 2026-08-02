using System.Globalization;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.Image;

/// <summary>
/// Traduit une demande de conversion d'image en arguments <c>magick</c>.
/// </summary>
/// <remarks>
/// ImageMagick lit les negatifs numeriques (CR2, NEF, ARW, DNG...) directement, via
/// libraw compile en dur plutot que par un delegue externe : confirme par
/// <c>magick -list format</c>, qui les liste tous en lecture seule (<c>r--</c>).
/// </remarks>
internal sealed class MagickArgsBuilder
{
    public IReadOnlyList<string> Build(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = request.Options as ImageOptions ?? new ImageOptions();
        var builder = new ArgumentBuilder()
            .Add(request.InputPath);

        if (options.AutoOrient)
        {
            builder.Add("-auto-orient");
        }

        ApplyWhiteBalance(builder, request.SourceFormat, options);

        if (!string.IsNullOrWhiteSpace(options.ColorSpace))
        {
            builder.Add("-colorspace", options.ColorSpace);
        }

        if (BuildResizeSpec(options) is { } resize)
        {
            builder.Add("-resize", resize);
        }

        builder.AddIf(!options.PreserveMetadata, "-strip");

        ApplyQuality(builder, request.TargetFormat, options);

        // Le format de sortie doit etre impose explicitement : l'extension du fichier
        // de travail est « .part », qui ne veut rien dire pour magick.
        builder.Add(FFormat(request.TargetFormat) + ":" + request.WorkingPath);

        return builder.Build();
    }

    /// <summary>
    /// Calcule la specification de redimensionnement ImageMagick.
    /// </summary>
    /// <remarks>
    /// Sans suffixe, <c>-resize LxH</c> fait deja tenir l'image dans la boite en
    /// conservant le ratio : c'est le comportement par defaut de magick, pas une option
    /// a activer. Le suffixe <c>!</c> force au contraire une deformation exacte, pour le
    /// cas plus rare ou l'utilisateur demande explicitement d'ignorer le ratio.
    /// </remarks>
    private static string? BuildResizeSpec(ImageOptions options)
    {
        if (options.Width is null && options.Height is null)
        {
            return null;
        }

        var width = options.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var height = options.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var spec = $"{width}x{height}";

        return options.PreserveAspectRatio ? spec : spec + "!";
    }

    /// <summary>
    /// Applique la qualite ou l'encodage sans perte, selon ce que le format cible accepte.
    /// </summary>
    private static void ApplyQuality(ArgumentBuilder builder, MediaFormat target, ImageOptions options)
    {
        switch (target.Id)
        {
            case "png":
                // Le curseur qualite de PNG regle le compromis vitesse/taille de la
                // compression Zlib, jamais la fidelite visuelle : PNG est toujours sans perte.
                builder.Add("-quality", Math.Clamp(options.Quality, 1, 100));
                break;

            case "webp":
                builder
                    .AddIf(options.Lossless, "-define", "webp:lossless=true")
                    .AddIf(!options.Lossless, "-quality", options.Quality.ToString(CultureInfo.InvariantCulture));
                break;

            case "avif" or "heic":
                builder
                    .AddIf(options.Lossless, "-define", "heic:lossless=true")
                    .AddIf(!options.Lossless, "-quality", options.Quality.ToString(CultureInfo.InvariantCulture));
                break;

            case "jpeg":
                builder.Add("-quality", options.Quality);
                builder.Add("-sampling-factor", "4:2:0");
                break;

            case "tiff" when options.Lossless:
                builder.Add("-compress", "LZW");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Applique une balance des blancs au dematricage RAW.
    /// </summary>
    /// <remarks>
    /// libraw applique par defaut la balance enregistree par le boitier au declenchement
    /// (<see cref="RawWhiteBalance.AsShot" />) : aucun argument n'est necessaire pour ce
    /// cas ni pour <see cref="RawWhiteBalance.Camera" />, qui s'appuie sur le meme
    /// mecanisme. Seul le mode <see cref="RawWhiteBalance.Auto" /> demande un traitement
    /// explicite, via l'operateur <c>-white-balance</c> applique apres dematricage — les
    /// parametres <c>-define raw:...</c> propres a libraw ne sont pas documentes de
    /// facon stable d'une version a l'autre, alors que cet operateur l'est.
    /// </remarks>
    private static void ApplyWhiteBalance(ArgumentBuilder builder, MediaFormat source, ImageOptions options)
    {
        if (source.Family != FormatFamily.RawImage || options.WhiteBalance != RawWhiteBalance.Auto)
        {
            return;
        }

        builder.Add("-white-balance");
    }

    /// <summary>Nom de coder ImageMagick pour un format, quand il differe de l'identifiant du catalogue.</summary>
    private static string FFormat(MediaFormat format) => format.Id switch
    {
        "jpeg" => "jpg",
        _ => format.Id,
    };
}
