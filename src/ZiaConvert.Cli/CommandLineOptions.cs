using System.Globalization;
using ZiaConvert.Core.Options;

namespace ZiaConvert.Cli;

internal enum CliCommand
{
    Convert,
    Probe,
    Engines,
    Formats,
    Help,
}

/// <summary>
/// Arguments de la ligne de commande.
/// </summary>
/// <remarks>
/// Analyseur ecrit a la main plutot que via une bibliotheque : la surface est reduite et
/// figee, et cela evite d'accrocher le projet a une API encore instable. Cette commande
/// est d'abord un outil de diagnostic du moteur, l'interface graphique arrivant ensuite.
/// </remarks>
internal sealed record CommandLineOptions
{
    public CliCommand Command { get; init; } = CliCommand.Help;

    public string? Input { get; init; }

    public string? Output { get; init; }

    public bool Overwrite { get; init; }

    public bool Verbose { get; init; }

    /// <summary>
    /// Affiche une estimation de la taille finale avant de lancer. Facultatif : contrairement
    /// a l'estimation de duree de l'agrandissement IA, l'ajouter par defaut alourdirait
    /// chaque conversion, y compris un remux qui prend deja moins d'une seconde.
    /// </summary>
    public bool EstimateSize { get; init; }

    public ConversionOptions BuildConversionOptions()
    {
        if (Gif is not null)
        {
            return Gif;
        }

        if (Upscale is not null)
        {
            return Upscale;
        }

        if (IsImageTarget)
        {
            return Image ?? new ImageOptions();
        }

        return IsAudioOnly ? Audio ?? new AudioOptions() : Video ?? new VideoOptions();
    }

    public VideoOptions? Video { get; init; }

    public AudioOptions? Audio { get; init; }

    public GifOptions? Gif { get; init; }

    public ImageOptions? Image { get; init; }

    public UpscaleOptions? Upscale { get; init; }

    public bool IsAudioOnly { get; init; }

    public bool IsImageTarget { get; init; }

    public string? Error { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLineOptions { Command = CliCommand.Help };
        }

        switch (args[0])
        {
            case "help" or "--help" or "-h":
                return new CommandLineOptions { Command = CliCommand.Help };

            case "engines":
                return new CommandLineOptions { Command = CliCommand.Engines };

            case "probe":
                return args.Length < 2
                    ? Fail("La commande « probe » attend un fichier.")
                    : new CommandLineOptions { Command = CliCommand.Probe, Input = args[1] };

            case "formats":
                return new CommandLineOptions
                {
                    Command = CliCommand.Formats,
                    Input = args.Length > 1 ? args[1] : null,
                };

            default:
                return ParseConvert(args);
        }
    }

    private static CommandLineOptions ParseConvert(string[] args)
    {
        string? input = null;
        string? output = null;
        var overwrite = false;
        var verbose = false;
        var estimateSize = false;

        var video = new VideoOptions();
        var audio = new AudioOptions();
        var gif = new GifOptions();
        var image = new ImageOptions();
        var upscale = new UpscaleOptions();
        var subtitles = new List<SubtitleImport>();
        var wantsGif = false;
        var wantsUpscale = false;
        var audioOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            string? Next(string name)
            {
                if (i + 1 < args.Length)
                {
                    return args[++i];
                }

                throw new CliParseException($"L'option « {name} » attend une valeur.");
            }

            try
            {
                switch (argument)
                {
                    case "-o" or "--output":
                        output = Next(argument);
                        break;

                    case "-y" or "--overwrite":
                        overwrite = true;
                        break;

                    case "-v" or "--verbose":
                        verbose = true;
                        break;

                    case "--estimate-size":
                        estimateSize = true;
                        break;

                    case "--codec":
                        video = video with { Codec = ParseEnum<VideoCodec>(Next(argument), argument) };
                        break;

                    case "--quality" or "-q":
                        var quality = ParseInt(Next(argument), argument);
                        video = video with { Quality = quality };
                        image = image with { Quality = quality };
                        break;

                    case "--width" or "-w":
                        var width = ParseInt(Next(argument), argument);
                        video = video with { Width = width };
                        gif = gif with { Width = width };
                        image = image with { Width = width };
                        break;

                    case "--height":
                        var height = ParseInt(Next(argument), argument);
                        video = video with { Height = height };
                        image = image with { Height = height };
                        break;

                    case "--no-aspect":
                        // Deforme l'image aux dimensions exactes plutot que de la faire
                        // simplement tenir dans la boite demandee.
                        image = image with { PreserveAspectRatio = false };
                        break;

                    case "--lossless":
                        image = image with { Lossless = true };
                        break;

                    case "--no-metadata":
                        image = image with { PreserveMetadata = false };
                        break;

                    case "--no-orient":
                        image = image with { AutoOrient = false };
                        break;

                    case "--colorspace":
                        image = image with { ColorSpace = Next(argument) ?? "sRGB" };
                        break;

                    case "--white-balance":
                        image = image with { WhiteBalance = ParseEnum<RawWhiteBalance>(Next(argument), argument) };
                        break;

                    case "--upscale":
                        // Reconstruction de detail par reseau de neurones, pas un simple
                        // redimensionnement : plusieurs secondes par image, GPU requis.
                        wantsUpscale = true;
                        break;

                    case "--factor":
                        upscale = upscale with { Factor = ParseInt(Next(argument), argument) };
                        break;

                    case "--model":
                        upscale = upscale with { Model = Next(argument) ?? upscale.Model };
                        break;

                    case "--tile":
                        upscale = upscale with { TileSize = ParseInt(Next(argument), argument) };
                        break;

                    case "--gpu":
                        upscale = upscale with { GpuId = ParseInt(Next(argument), argument) };
                        break;

                    case "--fps":
                        var fps = ParseDouble(Next(argument), argument);
                        video = video with { FrameRate = fps };
                        gif = gif with { FrameRate = fps };
                        break;

                    case "--interpolate":
                        // Fabrique les images intermediaires au lieu de les dupliquer.
                        video = video with { FrameRateMode = FrameRateMode.Interpolate };
                        break;

                    case "--hw":
                        video = video with { Hardware = ParseEnum<HardwareAcceleration>(Next(argument), argument) };
                        break;

                    case "--no-remux":
                        video = video with { AllowRemux = false };
                        break;

                    case "--remux-only":
                        // Echoue clairement plutot que de basculer sans prevenir sur un
                        // reencodage complet : utile pour un rip DVD qu'on veut seulement
                        // re-emballer, sans jamais toucher a l'image ni au son.
                        video = video with { RemuxOnly = true };
                        break;

                    case "--no-audio":
                        video = video with { RemoveAudio = true };
                        break;

                    case "--audio-track":
                        // Index absolu tel qu'affiche par « zia probe » : [1] Audio ...
                        video = video with { AudioTrackIndex = ParseInt(Next(argument), argument) };
                        break;

                    case "--subtitle-track":
                        video = video with { SubtitleTrackIndex = ParseInt(Next(argument), argument) };
                        break;

                    case "--add-subtitle":
                        // Un fichier .srt/.ass/.ssa/.vtt externe, integre a la sortie mkv.
                        // --subtitle-lang et --subtitle-title, s'ils suivent, s'appliquent
                        // a ce fichier-la.
                        var subtitlePath = Next(argument)
                            ?? throw new CliParseException($"L'option « {argument} » attend une valeur.");
                        subtitles.Add(new SubtitleImport { FilePath = subtitlePath });
                        break;

                    case "--subtitle-lang":
                        if (subtitles.Count == 0)
                        {
                            throw new CliParseException($"« {argument} » doit suivre un --add-subtitle.");
                        }

                        subtitles[^1] = subtitles[^1] with { Language = Next(argument) };
                        break;

                    case "--subtitle-title":
                        if (subtitles.Count == 0)
                        {
                            throw new CliParseException($"« {argument} » doit suivre un --add-subtitle.");
                        }

                        subtitles[^1] = subtitles[^1] with { Title = Next(argument) };
                        break;

                    case "--audio":
                        var codec = ParseEnum<AudioCodec>(Next(argument), argument);
                        video = video with { Audio = codec };
                        audio = audio with { Codec = codec };
                        break;

                    case "--bitrate" or "-b":
                        var bitrate = ParseBitrate(Next(argument), argument);
                        video = video with { AudioBitrate = bitrate };
                        audio = audio with { Bitrate = bitrate };
                        break;

                    case "--start" or "-ss":
                        var start = ParseTime(Next(argument), argument);
                        video = video with { StartTime = start };
                        audio = audio with { StartTime = start };
                        gif = gif with { StartTime = start };
                        break;

                    case "--end" or "-to":
                        var end = ParseTime(Next(argument), argument);
                        video = video with { EndTime = end };
                        audio = audio with { EndTime = end };
                        gif = gif with { EndTime = end };
                        break;

                    case "--normalize":
                        audio = audio with { Normalize = true };
                        audioOnly = true;
                        break;

                    default:
                        if (argument.StartsWith('-'))
                        {
                            return Fail($"Option inconnue : « {argument} ».");
                        }

                        if (input is not null)
                        {
                            return Fail($"Un seul fichier d'entree est accepte (« {argument} » est en trop).");
                        }

                        input = argument;
                        break;
                }
            }
            catch (CliParseException ex)
            {
                return Fail(ex.Message);
            }
        }

        if (input is null)
        {
            return Fail("Aucun fichier d'entree.");
        }

        if (output is null)
        {
            return Fail("Aucun fichier de sortie : utilisez -o.");
        }

        var targetExtension = Path.GetExtension(output).ToLowerInvariant();
        wantsGif = targetExtension == ".gif";
        audioOnly = audioOnly || IsAudioExtension(targetExtension);
        var imageTarget = !wantsGif && IsImageExtension(targetExtension);

        if (wantsUpscale && !imageTarget)
        {
            return Fail("--upscale ne s'applique qu'a une sortie image (jpg, png, webp...).");
        }

        if (subtitles.Count > 0)
        {
            video = video with { ExternalSubtitles = subtitles };
        }

        return new CommandLineOptions
        {
            Command = CliCommand.Convert,
            Input = input,
            Output = output,
            Overwrite = overwrite,
            Verbose = verbose,
            EstimateSize = estimateSize,
            Video = video,
            Audio = audio,
            Gif = wantsGif ? gif : null,
            Image = imageTarget && !wantsUpscale ? image : null,
            Upscale = wantsUpscale ? upscale : null,
            IsAudioOnly = audioOnly,
            IsImageTarget = imageTarget,
        };
    }

    private static bool IsAudioExtension(string extension) =>
        extension is ".mp3" or ".aac" or ".m4a" or ".flac" or ".wav" or ".opus" or ".ogg" or ".wma" or ".aiff";

    private static bool IsImageExtension(string extension) =>
        extension is ".jpg" or ".jpeg" or ".jpe" or ".png" or ".webp" or ".avif"
            or ".heic" or ".heif" or ".tif" or ".tiff" or ".bmp" or ".ico";

    private static CommandLineOptions Fail(string message) =>
        new() { Command = CliCommand.Help, Error = message };

    private static T ParseEnum<T>(string? value, string option)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new CliParseException(
                $"Valeur « {value} » invalide pour {option}. Attendu : {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");

    private static int ParseInt(string? value, string option) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new CliParseException($"Valeur « {value} » invalide pour {option} : un entier est attendu.");

    private static double ParseDouble(string? value, string option) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new CliParseException($"Valeur « {value} » invalide pour {option} : un nombre est attendu.");

    /// <summary>Accepte « 192k », « 192K » ou « 192000 ».</summary>
    private static long ParseBitrate(string? value, string option)
    {
        if (value is null)
        {
            throw new CliParseException($"L'option {option} attend une valeur.");
        }

        var multiplier = 1L;
        var text = value.Trim();

        if (text.EndsWith('k') || text.EndsWith('K'))
        {
            multiplier = 1_000L;
            text = text[..^1];
        }
        else if (text.EndsWith('m') || text.EndsWith('M'))
        {
            multiplier = 1_000_000L;
            text = text[..^1];
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed * multiplier
            : throw new CliParseException($"Debit « {value} » invalide pour {option}. Exemples : 192k, 2M, 320000.");
    }

    /// <summary>Accepte « 90 » (secondes), « 1:30 » ou « 00:01:30.5 ».</summary>
    private static TimeSpan ParseTime(string? value, string option)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new CliParseException($"Duree « {value} » invalide pour {option}. Exemples : 90, 1:30, 00:01:30.5.");
    }
}

internal sealed class CliParseException : Exception
{
    public CliParseException(string message)
        : base(message)
    {
    }
}
