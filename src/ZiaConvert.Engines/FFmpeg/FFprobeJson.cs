using System.Text.Json.Serialization;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Reflet direct de la sortie <c>ffprobe -print_format json</c>. Tous les champs sont
/// optionnels : ffprobe omet ce qui ne s'applique pas au fichier, et rend <c>"N/A"</c>
/// pour ce qu'il ne sait pas calculer.
/// </summary>
internal sealed record FFprobeOutput
{
    [JsonPropertyName("streams")]
    public List<FFprobeStream>? Streams { get; init; }

    [JsonPropertyName("format")]
    public FFprobeFormat? Format { get; init; }
}

internal sealed record FFprobeFormat
{
    [JsonPropertyName("format_name")]
    public string? FormatName { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; init; }
}

internal sealed record FFprobeStream
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; init; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; init; }

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("pix_fmt")]
    public string? PixelFormat { get; init; }

    /// <summary>Cadence sous forme de fraction, par exemple <c>30000/1001</c> pour du 29,97.</summary>
    [JsonPropertyName("r_frame_rate")]
    public string? FrameRate { get; init; }

    [JsonPropertyName("avg_frame_rate")]
    public string? AverageFrameRate { get; init; }

    [JsonPropertyName("channels")]
    public int? Channels { get; init; }

    [JsonPropertyName("sample_rate")]
    public string? SampleRate { get; init; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }
}
