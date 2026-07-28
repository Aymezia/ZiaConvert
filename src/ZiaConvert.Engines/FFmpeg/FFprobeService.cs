using System.Globalization;
using System.Text.Json;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Sonde un fichier via <c>ffprobe</c>. Deux informations en sortent qui conditionnent
/// tout le reste : la duree, sans laquelle aucun pourcentage n'est calculable, et la liste
/// des codecs, qui decide de la possibilite d'un remux.
/// </summary>
public sealed class FFprobeService : IMediaProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;

    public FFprobeService(IProcessRunner runner, IEngineLocator locator)
    {
        _runner = runner;
        _locator = locator;
    }

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ConversionException($"Le fichier « {Path.GetFileName(path)} » est introuvable.");
        }

        var ffprobe = _locator.Locate("ffprobe")
            ?? throw new ConversionException("ffprobe est introuvable : le moteur video n'est pas installe.");

        var arguments = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")
            .Add("-print_format", "json")
            .Add("-show_format")
            .Add("-show_streams")
            .Add(path)
            .Build();

        var result = await _runner
            .RunAsync(new ProcessRequest { FileName = ffprobe, Arguments = arguments }, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            throw new ConversionException(
                $"Impossible de lire « {Path.GetFileName(path)} » : le fichier est corrompu ou dans un format non reconnu.",
                "ffprobe",
                result.StandardError);
        }

        return Parse(result.StandardOutputText, path);
    }

    private static MediaInfo Parse(string json, string path)
    {
        FFprobeOutput? output;

        try
        {
            output = JsonSerializer.Deserialize<FFprobeOutput>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConversionException($"Sortie de ffprobe illisible pour « {Path.GetFileName(path)} ».", ex);
        }

        if (output is null)
        {
            throw new ConversionException($"ffprobe n'a rien renvoye pour « {Path.GetFileName(path)} ».");
        }

        var streams = (output.Streams ?? [])
            .Select(ToStreamInfo)
            .ToList();

        return new MediaInfo
        {
            FormatName = output.Format?.FormatName,
            Duration = ParseDuration(output.Format?.Duration) ?? LongestStreamDuration(output.Streams),
            SizeBytes = ParseLong(output.Format?.Size) ?? 0L,
            Streams = streams,
        };
    }

    private static MediaStreamInfo ToStreamInfo(FFprobeStream stream) => new()
    {
        Index = stream.Index,
        Kind = stream.CodecType switch
        {
            "video" => MediaStreamKind.Video,
            "audio" => MediaStreamKind.Audio,
            "subtitle" => MediaStreamKind.Subtitle,
            "attachment" => MediaStreamKind.Attachment,
            _ => MediaStreamKind.Unknown,
        },
        CodecName = stream.CodecName ?? "unknown",
        Profile = stream.Profile,
        Width = stream.Width,
        Height = stream.Height,
        PixelFormat = stream.PixelFormat,

        // On prefere la cadence moyenne : sur un fichier a debit variable, r_frame_rate
        // rend la cadence maximale theorique, souvent aberrante (1000 im/s par exemple).
        FrameRate = ParseRational(stream.AverageFrameRate) ?? ParseRational(stream.FrameRate),
        Channels = stream.Channels,
        SampleRate = ParseInt(stream.SampleRate),
        BitRate = ParseLong(stream.BitRate),
    };

    /// <summary>
    /// Certains conteneurs, MKV en tete, n'annoncent pas de duree globale. On retombe alors
    /// sur la plus longue des pistes.
    /// </summary>
    private static TimeSpan? LongestStreamDuration(List<FFprobeStream>? streams)
    {
        if (streams is null or { Count: 0 })
        {
            return null;
        }

        var durations = streams
            .Select(s => ParseDuration(s.Duration))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

        return durations.Count > 0 ? durations.Max() : null;
    }

    /// <summary>Convertit une fraction <c>30000/1001</c> en nombre decimal.</summary>
    internal static double? ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf('/', StringComparison.Ordinal);

        if (separator < 0)
        {
            return ParseDouble(value);
        }

        var numerator = ParseDouble(value[..separator]);
        var denominator = ParseDouble(value[(separator + 1)..]);

        // ffprobe rend « 0/0 » pour les pistes sans cadence, les pistes audio notamment.
        return numerator is null || denominator is null or 0d ? null : numerator / denominator;
    }

    internal static TimeSpan? ParseDuration(string? value) =>
        ParseDouble(value) is { } seconds && seconds >= 0d ? TimeSpan.FromSeconds(seconds) : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
