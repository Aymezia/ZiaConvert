namespace ZiaConvert.Core.Options;

/// <summary>Reglages d'une extraction ou conversion audio.</summary>
public sealed record AudioOptions : ConversionOptions
{
    public AudioCodec Codec { get; init; } = AudioCodec.Auto;

    /// <summary>Debit en bits/s (ex. <c>192_000</c>). Ignore pour les codecs sans perte.</summary>
    public long? Bitrate { get; init; }

    public int? SampleRate { get; init; }

    /// <summary>Nombre de canaux : <c>1</c> mono, <c>2</c> stereo. <c>null</c> conserve la source.</summary>
    public int? Channels { get; init; }

    /// <summary>Applique une normalisation de loudness EBU R128.</summary>
    public bool Normalize { get; init; }

    public TimeSpan? StartTime { get; init; }

    public TimeSpan? EndTime { get; init; }
}
