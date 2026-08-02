using System.Globalization;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;
using ZiaConvert.Engines;
using ZiaConvert.Engines.Upscale;

namespace ZiaConvert.Cli;

/// <summary>
/// Point d'entree de la commande <c>zia</c>.
/// </summary>
/// <remarks>
/// Cette commande sert autant a convertir qu'a diagnostiquer : <c>engines</c> et
/// <c>probe</c> repondent aux deux questions qui reviennent le plus quand une conversion
/// se passe mal, a savoir ce que la machine sait faire et ce que contient le fichier.
/// </remarks>
internal static class CliRunner
{
    private const int Success = 0;
    private const int Failure = 1;
    private const int Cancelled = 130;

    public static async Task<int> RunAsync(string[] args)
    {
        UseUtf8Output();

        var options = CommandLineOptions.Parse(args);

        if (options.Error is { } error)
        {
            Console.Error.WriteLine($"Erreur : {error}");
            Console.Error.WriteLine();
            WriteHelp();
            return Failure;
        }

        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            // On intercepte pour laisser ffmpeg fermer proprement son conteneur ; sans cela
            // le systeme tuerait le processus et laisserait un fichier inutilisable.
            e.Cancel = true;
            Console.Error.WriteLine();
            Console.Error.WriteLine("Annulation en cours...");
            cancellation.Cancel();
        };

        try
        {
            return options.Command switch
            {
                CliCommand.Convert => await ConvertAsync(options, cancellation.Token).ConfigureAwait(false),
                CliCommand.Probe => await ProbeAsync(options, cancellation.Token).ConfigureAwait(false),
                CliCommand.Engines => await EnginesAsync(cancellation.Token).ConfigureAwait(false),
                CliCommand.Formats => Formats(options),
                _ => WriteHelp(),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Conversion annulee.");
            return Cancelled;
        }
        catch (ConversionException ex)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");

            foreach (var line in ex.EngineOutput.TakeLast(5))
            {
                Console.Error.WriteLine($"  {line}");
            }

            return Failure;
        }
    }

    /// <summary>
    /// Bascule la console en UTF-8. Sans cela, la page de codes par defaut de Windows
    /// remplace les accents et les guillemets francais par des points d'interrogation.
    /// </summary>
    private static void UseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Sortie redirigee vers une cible qui refuse le changement d'encodage :
            // l'affichage sera degrade, mais la conversion doit se faire quand meme.
        }
    }

    private static async Task<int> ConvertAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var services = ConversionServices.Create();

        var request = services.Router.CreateRequest(
            options.Input!,
            options.Output!,
            options.BuildConversionOptions(),
            options.Overwrite);

        if (!File.Exists(request.InputPath))
        {
            Console.Error.WriteLine($"Erreur : « {options.Input} » est introuvable.");
            return Failure;
        }

        if (options.Video?.ExternalSubtitles is { Count: > 0 } subtitles)
        {
            foreach (var subtitle in subtitles)
            {
                if (!File.Exists(subtitle.FilePath))
                {
                    Console.Error.WriteLine($"Erreur : sous-titre « {subtitle.FilePath} » introuvable.");
                    return Failure;
                }
            }
        }

        Console.Error.WriteLine(
            $"{Path.GetFileName(request.InputPath)}  ->  {Path.GetFileName(request.OutputPath)}");

        if (options.Upscale is { } upscale)
        {
            await ShowUpscaleEstimateAsync(services, request.InputPath, upscale, cancellationToken).ConfigureAwait(false);
        }

        if (options.EstimateSize)
        {
            await ShowSizeEstimateAsync(services, request, cancellationToken).ConfigureAwait(false);
        }

        var bar = new ConsoleProgressBar();
        var progress = new Progress<ConversionProgress>(bar.Report);

        var result = await services.Executor
            .ExecuteAsync(request, progress, cancellationToken)
            .ConfigureAwait(false);

        bar.Clear();

        if (!result.Success)
        {
            Console.Error.WriteLine($"Echec : {result.ErrorMessage}");
            return Failure;
        }

        var seconds = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        Console.Error.WriteLine($"Termine en {seconds} s — {FormatSize(result.OutputSizeBytes)}");

        if (result.Detail is { Length: > 0 } detail)
        {
            Console.Error.WriteLine($"  {detail}");
        }

        if (result.VerificationWarning is { Length: > 0 } warning)
        {
            Console.Error.WriteLine($"  ATTENTION : {warning}");
        }

        // Le chemin produit part sur la sortie standard : il reste ainsi chainable.
        Console.WriteLine(result.OutputPath);

        return Success;
    }

    /// <summary>
    /// Affiche une estimation de duree avant de lancer un agrandissement : c'est une
    /// operation de plusieurs secondes par image, pas instantanee comme le reste des
    /// conversions, et l'utilisateur doit pouvoir juger avant de s'engager.
    /// </summary>
    private static async Task ShowUpscaleEstimateAsync(
        ConversionServices services,
        string inputPath,
        UpscaleOptions upscale,
        CancellationToken cancellationToken)
    {
        var engine = services.Engines.OfType<RealEsrganEngine>().FirstOrDefault();

        if (engine is null)
        {
            return;
        }

        var dimensions = await TryGetImageDimensionsAsync(services, inputPath, cancellationToken).ConfigureAwait(false);

        if (dimensions is not { } size)
        {
            return;
        }

        var estimate = await engine
            .EstimateDurationAsync(size.Width, size.Height, upscale, cancellationToken)
            .ConfigureAwait(false);

        if (estimate is { } duration)
        {
            var seconds = duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            var outputWidth = size.Width * upscale.Factor;
            var outputHeight = size.Height * upscale.Factor;

            Console.Error.WriteLine(
                $"Duree estimee : ~{seconds} s  ({size.Width}x{size.Height} -> {outputWidth}x{outputHeight})");
        }
    }

    /// <summary>
    /// Affiche une estimation de la taille finale avant de lancer une conversion video.
    /// </summary>
    /// <remarks>
    /// Facultatif (voir <see cref="CommandLineOptions.EstimateSize" />) : contrairement a
    /// l'estimation de duree de l'agrandissement IA, un remux prend deja moins d'une
    /// seconde — ajouter un echantillonnage systematique alourdirait ce cas le plus
    /// courant pour un interet marginal.
    /// </remarks>
    private static async Task ShowSizeEstimateAsync(
        ConversionServices services,
        ConversionRequest request,
        CancellationToken cancellationToken)
    {
        var prepared = await services.Router.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        var estimate = await services.FileSizeEstimator.EstimateAsync(prepared, cancellationToken).ConfigureAwait(false);

        if (estimate is null)
        {
            return;
        }

        var label = estimate.IsSampled
            ? "Taille estimee (extrapolee sur un extrait)"
            : "Taille estimee (remux, quasi exacte)";

        Console.Error.WriteLine($"{label} : ~{FormatSize(estimate.EstimatedBytes)}");
    }

    /// <summary>Sonde les dimensions d'une image via ImageMagick, sans lien avec le moteur d'agrandissement.</summary>
    private static async Task<(int Width, int Height)?> TryGetImageDimensionsAsync(
        ConversionServices services,
        string path,
        CancellationToken cancellationToken)
    {
        var magick = services.Locator.Locate("magick");

        if (magick is null)
        {
            return null;
        }

        var result = await services.ProcessRunner.RunAsync(
            new ProcessRequest
            {
                FileName = magick,
                Arguments = ["identify", "-format", "%w %h", path],
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return null;
        }

        var parts = result.StandardOutputText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : null;
    }

    private static async Task<int> ProbeAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var services = ConversionServices.Create();
        var info = await services.Probe.ProbeAsync(options.Input!, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Fichier   : {Path.GetFileName(options.Input)}");
        Console.WriteLine($"Conteneur : {info.FormatName ?? "inconnu"}");
        Console.WriteLine($"Duree     : {(info.Duration is { } d ? Format(d) : "inconnue")}");
        Console.WriteLine($"Taille    : {FormatSize(info.SizeBytes)}");
        Console.WriteLine();

        foreach (var stream in info.Streams)
        {
            var description = stream.Kind switch
            {
                MediaStreamKind.Video =>
                    $"{stream.CodecName} {stream.Width}x{stream.Height}" +
                    (stream.FrameRate is { } fps ? $" @ {fps.ToString("0.##", CultureInfo.InvariantCulture)} im/s" : string.Empty) +
                    (stream.PixelFormat is { } pix ? $" ({pix})" : string.Empty),

                MediaStreamKind.Audio =>
                    $"{stream.CodecName} {stream.Channels} canaux" +
                    (stream.SampleRate is { } rate ? $" @ {rate} Hz" : string.Empty),

                _ => stream.CodecName,
            };

            Console.WriteLine($"  [{stream.Index}] {stream.Kind,-10} {description}");
        }

        return Success;
    }

    private static async Task<int> EnginesAsync(CancellationToken cancellationToken)
    {
        var services = ConversionServices.Create();

        Console.WriteLine("Moteurs");

        foreach (var engine in services.Engines)
        {
            var availability = engine.CheckAvailability();
            var status = availability.IsAvailable ? "disponible" : $"absent — {availability.Reason}";

            Console.WriteLine($"  {engine.Name,-12} {status}");
        }

        Console.WriteLine();
        Console.WriteLine("Binaires");

        foreach (var tool in new[] { "ffmpeg", "ffprobe", "magick", "soffice", "realesrgan-ncnn-vulkan" })
        {
            Console.WriteLine($"  {tool,-24} {services.Locator.Locate(tool) ?? "introuvable"}");
        }

        Console.WriteLine();
        Console.Write("Encodeurs materiels (test d'encodage reel en cours)...");

        var hardware = await services.Hardware.DetectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        Console.WriteLine("\r" + new string(' ', 60) + "\r");
        Console.WriteLine("Encodeurs materiels");

        if (hardware.HasAnyHardware)
        {
            foreach (var encoder in hardware.WorkingEncoders)
            {
                Console.WriteLine($"  {encoder}");
            }

            Console.WriteLine();
            Console.WriteLine($"  Famille retenue : {hardware.Preferred}");
        }
        else
        {
            Console.WriteLine("  aucun — les conversions se feront en logiciel");
        }

        return Success;
    }

    private static int Formats(CommandLineOptions options)
    {
        var services = ConversionServices.Create();

        if (options.Input is { } input)
        {
            var source = services.Formats.FindByPath(input);

            if (source is null)
            {
                Console.Error.WriteLine($"Format inconnu pour « {input} ».");
                return Failure;
            }

            Console.WriteLine($"Source : {source.DisplayName} ({source.Family})");
            Console.WriteLine();
            Console.WriteLine("Sorties possibles :");

            foreach (var group in services.Formats.TargetsFor(source).GroupBy(f => f.Family))
            {
                Console.WriteLine($"  {group.Key}");
                Console.WriteLine("    " + string.Join(" ", group.Select(f => f.PrimaryExtension)));
            }

            return Success;
        }

        foreach (var group in services.Formats.All.GroupBy(f => f.Family))
        {
            Console.WriteLine(group.Key.ToString());
            Console.WriteLine("  " + string.Join(" ", group.Select(f => f.PrimaryExtension)));
            Console.WriteLine();
        }

        return Success;
    }

    private static string Format(TimeSpan value) =>
        value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss\.ff", CultureInfo.InvariantCulture);

    private static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        return $"{size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static int WriteHelp()
    {
        Console.WriteLine("""
            zia — convertisseur universel

            UTILISATION
              zia <entree> -o <sortie> [options]
              zia probe <fichier>          Analyse un fichier
              zia engines                  Moteurs et encodeurs materiels
              zia formats [fichier]        Formats connus, ou sorties possibles

            OPTIONS
              -o, --output <chemin>   Fichier de sortie (le format vient de l'extension)
              -y, --overwrite         Ecrase la sortie si elle existe
              -v, --verbose           Detaille les commandes executees
                  --estimate-size     Affiche la taille finale estimee avant de lancer
                                      (video uniquement ; encode un court extrait pour
                                      les reglages a qualite constante, exact pour un remux)

            VIDEO
              --codec <nom>           auto, copy, h264, h265, av1, vp9, prores
              -q, --quality <n>       Qualite constante, plus bas = meilleur (18-28)
              -w, --width <n>         Largeur cible, hauteur deduite du ratio
                  --height <n>        Hauteur cible
                  --fps <n>           Cadence de sortie (duplique les images)
                  --interpolate       Fabrique les images intermediaires plutot que de
                                      les dupliquer : seule facon de gagner en fluidite,
                                      mais beaucoup plus lent
                  --hw <nom>          auto, none, nvenc, quicksync, amf
                  --no-remux          Force le reencodage meme si la copie suffisait
                  --remux-only        Echoue si la copie de flux est impossible, plutot
                                      que de basculer sans prevenir sur un reencodage
                  --no-audio          Supprime la piste audio
                  --audio-track <n>   Piste audio a garder (index vu par « zia probe »),
                                      utile pour un rip multilingue ou avec commentaire
                  --subtitle-track <n>  Une seule piste de sous-titres a garder
                  --add-subtitle <chemin>  Integre un fichier .srt/.ass/.ssa/.vtt externe
                                      a la sortie (mkv uniquement) ; repetable pour
                                      plusieurs langues
                  --subtitle-lang <code>  Langue de la derniere piste ajoutee (ex. fre, eng)
                  --subtitle-title <texte>  Nom affiche par le lecteur pour cette piste

            AUDIO
              --audio <nom>           auto, copy, aac, mp3, opus, flac, vorbis, pcm, none
              -b, --bitrate <debit>   192k, 2M, 320000
              --normalize             Normalisation de loudness EBU R128

            DECOUPE
              -ss, --start <duree>    90, 1:30 ou 00:01:30.5
              -to, --end <duree>

            IMAGE (RAW compris : CR2, CR3, NEF, ARW, DNG, ORF, RW2, RAF, PEF, SRW)
              -q, --quality <n>       Qualite 1-100 (formats avec perte)
              -w, --width <n>         Largeur cible, hauteur deduite du ratio
                  --height <n>        Hauteur cible
                  --no-aspect         Deforme aux dimensions exactes plutot que d'ajuster
                  --lossless          Encodage sans perte (webp, avif, heic)
                  --no-metadata       Efface les donnees EXIF/IPTC/XMP
                  --no-orient         Ne pas appliquer la rotation EXIF automatiquement
                  --colorspace <nom>  sRGB par defaut
                  --white-balance <nom>  asshot (defaut), auto, camera — RAW uniquement

            AGRANDISSEMENT PAR IA (Real-ESRGAN, GPU requis)
              --upscale               Reconstruit du detail plutot que d'etirer les pixels :
                                      plusieurs secondes par image, contrairement a -w/--height
                                      qui redimensionne instantanement sans ajouter de detail
                  --factor <n>        2, 3 ou 4 (defaut 4)
                  --model <nom>       realesrgan-x4plus (defaut), realesrgan-x4plus-anime,
                                      realesr-animevideov3
                  --tile <n>          Taille des tuiles, 0=automatique (defaut)
                  --gpu <n>           Index du GPU, absent=automatique

            EXEMPLES
              zia film.mp4 -o film.mkv
                  Copie des flux, quelques secondes, sans perte

              zia film.mkv -o film.mp4 --codec h264 -q 20
                  Reencodage, acceleration materielle si disponible

              zia clip.mp4 -o clip.gif --fps 15 -w 480 -ss 5 -to 10

              zia concert.mp4 -o concert.mp3 -b 192k

              zia video.mp4 -o video-4k.mp4 -w 3840
                  Agrandissement par interpolation, sans ajout de detail

              zia photo.cr2 -o photo.jpg
                  Developpement RAW avec la balance des blancs du boitier

              zia photo.jpg -o photo.webp -q 85

              zia vieille-photo.jpg -o vieille-photo-hd.jpg --upscale --factor 4
                  Affiche une estimation de duree avant de lancer

              zia rip_dvd.vob -o film.mkv --remux-only
                  Re-emballe un rip DVD sans reencoder ; echoue clairement si
                  les codecs source (MPEG-2, AC3...) ne rentrent pas dans mkv

              zia rip_dvd.mkv -o film.mkv --audio-track 2 --subtitle-track 4
                  Garde une piste audio et une piste de sous-titres precises
                  (indices vus par « zia probe ») sur un rip multipiste

              zia film.mkv -o film.mkv --add-subtitle vostfr.srt --subtitle-lang fre --subtitle-title VOSTFR
                  Integre un sous-titre externe a la sortie, sans reencoder
                  (copie de flux si le reste le permet)

              zia film.mkv -o film.mkv --add-subtitle en.srt --subtitle-lang eng --add-subtitle fr.srt --subtitle-lang fre
                  Plusieurs langues : --subtitle-lang s'applique au dernier
                  --add-subtitle rencontre

              zia film.mkv -o film.mp4 --codec h265 -q 22 --estimate-size
                  Affiche la taille finale avant meme de lancer l'encodage
            """);

        return Success;
    }
}
