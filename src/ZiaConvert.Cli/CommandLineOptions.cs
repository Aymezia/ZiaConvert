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

    public ConversionOptions BuildConversionOptions()
    {
        if (Gif is not null)
        {
            return Gif;
        }

        return IsAudioOnly ? Audio ?? new AudioOptions() : Video ?? new VideoOptions();
    }

    public VideoOptions? Video { get; init; }

    public AudioOptions? Audio { get; init; }

    public GifOptions? Gif { get; init; }

    public bool IsAudioOnly { get; init; }

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

        var video = new VideoOptions();
        var audio = new AudioOptions();
        var gif = new GifOptions();
        var wantsGif = false;
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

                    case "--codec":
                        video = video with { Codec = ParseEnum<VideoCodec>(Next(argument), argument) };
                        break;

                    case "--quality" or "-q":
                        video = video with { Quality = ParseInt(Next(argument), argument) };
                        break;

                    case "--width" or "-w":
                        var width = ParseInt(Next(argument), argument);
                        video = video with { Width = width };
                        gif = gif with { Width = width };
                        break;

                    case "--height":
                        video = video with { Height = ParseInt(Next(argument), argument) };
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

                    case "--no-audio":
                        video = video with { RemoveAudio = true };
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

        return new CommandLineOptions
        {
            Command = CliCommand.Convert,
            Input = input,
            Output = output,
            Overwrite = overwrite,
            Verbose = verbose,
            Video = video,
            Audio = audio,
            Gif = wantsGif ? gif : null,
            IsAudioOnly = audioOnly,
        };
    }

    private static bool IsAudioExtension(string extension) =>
        extension is ".mp3" or ".aac" or ".m4a" or ".flac" or ".wav" or ".opus" or ".ogg" or ".wma" or ".aiff";

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
