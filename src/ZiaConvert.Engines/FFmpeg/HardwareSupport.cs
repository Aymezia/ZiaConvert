using System.Text.Json.Serialization;
using ZiaConvert.Core.Options;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>Encodeurs materiels reellement utilisables sur cette machine.</summary>
public sealed record HardwareSupport
{
    /// <summary>Noms ffmpeg des encodeurs ayant passe un encodage de test (ex. <c>h264_nvenc</c>).</summary>
    [JsonPropertyName("workingEncoders")]
    public IReadOnlyList<string> WorkingEncoders { get; init; } = [];

    /// <summary>Signature du binaire ffmpeg ayant servi a la detection, pour invalider le cache.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonIgnore]
    public bool HasAnyHardware => WorkingEncoders.Count > 0;

    /// <summary>Famille materielle disponible, deduite des encodeurs qui fonctionnent.</summary>
    [JsonIgnore]
    public HardwareAcceleration Preferred =>
        WorkingEncoders.Any(e => e.EndsWith("_nvenc", StringComparison.Ordinal)) ? HardwareAcceleration.Nvenc
        : WorkingEncoders.Any(e => e.EndsWith("_qsv", StringComparison.Ordinal)) ? HardwareAcceleration.QuickSync
        : WorkingEncoders.Any(e => e.EndsWith("_amf", StringComparison.Ordinal)) ? HardwareAcceleration.Amf
        : WorkingEncoders.Any(e => e.EndsWith("_videotoolbox", StringComparison.Ordinal)) ? HardwareAcceleration.VideoToolbox
        : HardwareAcceleration.None;

    public bool Supports(string encoderName) =>
        WorkingEncoders.Contains(encoderName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Choisit l'encodeur pour un codec donne.
    /// </summary>
    /// <remarks>
    /// Le repli logiciel est systematique : une machine sans GPU compatible, ou dont le
    /// pilote refuse l'encodage, doit convertir quand meme — plus lentement, mais sans erreur.
    /// </remarks>
    public string ResolveEncoder(VideoCodec codec, HardwareAcceleration requested)
    {
        var software = SoftwareEncoder(codec);

        if (requested == HardwareAcceleration.None)
        {
            return software;
        }

        var suffix = requested == HardwareAcceleration.Auto
            ? SuffixFor(Preferred)
            : SuffixFor(requested);

        // VP9 et ProRes n'ont pas d'equivalent materiel repandu : ils restent en logiciel.
        if (suffix is null || CodecPrefix(codec) is not { } prefix)
        {
            return software;
        }

        var candidate = prefix + suffix;

        return Supports(candidate) ? candidate : software;
    }

    internal static string SoftwareEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "libx264",
        VideoCodec.H265 => "libx265",
        VideoCodec.Av1 => "libsvtav1",
        VideoCodec.Vp9 => "libvpx-vp9",
        VideoCodec.ProRes => "prores_ks",
        _ => "libx264",
    };

    private static string? CodecPrefix(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.H265 => "hevc",
        VideoCodec.Av1 => "av1",
        _ => null,
    };

    private static string? SuffixFor(HardwareAcceleration acceleration) => acceleration switch
    {
        HardwareAcceleration.Nvenc => "_nvenc",
        HardwareAcceleration.QuickSync => "_qsv",
        HardwareAcceleration.Amf => "_amf",
        HardwareAcceleration.VideoToolbox => "_videotoolbox",
        _ => null,
    };
}
