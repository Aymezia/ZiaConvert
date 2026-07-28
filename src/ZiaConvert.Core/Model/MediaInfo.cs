namespace ZiaConvert.Core.Model;

/// <summary>
/// Metadonnees d'un fichier source, telles que renvoyees par une sonde (ffprobe).
/// Sert notamment a decider si une conversion peut se faire en simple remux.
/// </summary>
public sealed record MediaInfo
{
    /// <summary>Nom du conteneur detecte (ex. <c>mov,mp4,m4a,3gp,3g2,mj2</c>).</summary>
    public string? FormatName { get; init; }

    public TimeSpan? Duration { get; init; }

    public long SizeBytes { get; init; }

    public IReadOnlyList<MediaStreamInfo> Streams { get; init; } = [];

    public MediaStreamInfo? PrimaryVideo =>
        Streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Video);

    public MediaStreamInfo? PrimaryAudio =>
        Streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Audio);

    public bool HasVideo => PrimaryVideo is not null;

    public bool HasAudio => PrimaryAudio is not null;
}

public enum MediaStreamKind
{
    Unknown = 0,
    Video,
    Audio,
    Subtitle,
    Attachment,
}

public sealed record MediaStreamInfo
{
    public required int Index { get; init; }

    public required MediaStreamKind Kind { get; init; }

    /// <summary>Nom court du codec tel que rapporte par ffprobe (ex. <c>h264</c>, <c>aac</c>).</summary>
    public required string CodecName { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public int? Channels { get; init; }

    public int? SampleRate { get; init; }

    public long? BitRate { get; init; }

    public string? PixelFormat { get; init; }

    /// <summary>Profil du codec (ex. <c>High</c>, <c>Main 10</c>) : utile pour verifier la compatibilite d'un conteneur.</summary>
    public string? Profile { get; init; }
}
