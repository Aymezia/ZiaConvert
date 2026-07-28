using System.Globalization;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Engines;

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

        Console.Error.WriteLine(
            $"{Path.GetFileName(request.InputPath)}  ->  {Path.GetFileName(request.OutputPath)}");

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

        // Le chemin produit part sur la sortie standard : il reste ainsi chainable.
        Console.WriteLine(result.OutputPath);

        return Success;
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
                  --no-audio          Supprime la piste audio

            AUDIO
              --audio <nom>           auto, copy, aac, mp3, opus, flac, vorbis, pcm, none
              -b, --bitrate <debit>   192k, 2M, 320000
              --normalize             Normalisation de loudness EBU R128

            DECOUPE
              -ss, --start <duree>    90, 1:30 ou 00:01:30.5
              -to, --end <duree>

            EXEMPLES
              zia film.mp4 -o film.mkv
                  Copie des flux, quelques secondes, sans perte

              zia film.mkv -o film.mp4 --codec h264 -q 20
                  Reencodage, acceleration materielle si disponible

              zia clip.mp4 -o clip.gif --fps 15 -w 480 -ss 5 -to 10

              zia concert.mp4 -o concert.mp3 -b 192k

              zia video.mp4 -o video-4k.mp4 -w 3840
                  Agrandissement par interpolation, sans ajout de detail
            """);

        return Success;
    }
}
