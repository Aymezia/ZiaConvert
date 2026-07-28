using ZiaConvert.Core.Options;

namespace ZiaConvert.App.ViewModels;

/// <summary>
/// Entrees des listes deroulantes.
/// </summary>
/// <remarks>
/// Chaque choix porte un libelle en clair et une explication courte. Les valeurs brutes
/// de ffmpeg — <c>libx264</c>, <c>crf</c>, <c>yuv420p</c> — ne veulent rien dire pour
/// quelqu'un qui veut juste convertir une video, mais restent visibles pour qui les cherche.
/// </remarks>
public sealed record CodecChoice(string Label, VideoCodec Value, string Hint)
{
    public static IReadOnlyList<CodecChoice> All { get; } =
    [
        new("Automatique", VideoCodec.Auto, "Choisi selon le format de sortie"),
        new("H.264", VideoCodec.H264, "Lisible partout, le choix sûr"),
        new("H.265 / HEVC", VideoCodec.H265, "Deux fois plus compact, moins universel"),
        new("AV1", VideoCodec.Av1, "Le plus compact, lecture recente requise"),
        new("VP9", VideoCodec.Vp9, "Pour le WebM et le web"),
        new("ProRes", VideoCodec.ProRes, "Montage video, fichiers tres volumineux"),
    ];

    public override string ToString() => Label;
}

public sealed record AudioCodecChoice(string Label, AudioCodec Value, string Hint)
{
    public static IReadOnlyList<AudioCodecChoice> All { get; } =
    [
        new("Automatique", AudioCodec.Auto, "Choisi selon le format de sortie"),
        new("Conserver tel quel", AudioCodec.Copy, "Aucune perte, aucun calcul"),
        new("AAC", AudioCodec.Aac, "Le standard actuel"),
        new("MP3", AudioCodec.Mp3, "Compatible avec tout, meme ancien"),
        new("Opus", AudioCodec.Opus, "Meilleure qualite a debit egal"),
        new("FLAC", AudioCodec.Flac, "Sans perte, fichiers volumineux"),
        new("Vorbis", AudioCodec.Vorbis, "Pour l'Ogg et le WebM"),
        new("PCM (non compresse)", AudioCodec.Pcm, "Pour le WAV et le montage"),
    ];

    public override string ToString() => Label;
}

public sealed record HardwareChoice(string Label, HardwareAcceleration Value, string Hint)
{
    public static IReadOnlyList<HardwareChoice> All { get; } =
    [
        new("Automatique", HardwareAcceleration.Auto, "Utilise le GPU s'il sait le faire"),
        new("Processeur seul", HardwareAcceleration.None, "Plus lent, qualite legerement superieure"),
        new("NVIDIA (NVENC)", HardwareAcceleration.Nvenc, "Cartes GeForce et Quadro"),
        new("Intel (QuickSync)", HardwareAcceleration.QuickSync, "Processeurs Intel avec graphique integre"),
        new("AMD (AMF)", HardwareAcceleration.Amf, "Cartes Radeon"),
    ];

    public override string ToString() => Label;
}

/// <summary>
/// Resolution exprimee en hauteur, comme on en parle couramment : 1080p, 720p.
/// La largeur suit le rapport d'origine.
/// </summary>
public sealed record ResolutionChoice(string Label, int? Height, string Hint)
{
    public bool IsCustom => Height is null && Label.StartsWith("Personnalis", StringComparison.Ordinal);

    public static IReadOnlyList<ResolutionChoice> All { get; } =
    [
        new("Conserver la source", null, "Aucun redimensionnement"),
        new("4K — 2160p", 2160, "Agrandissement : etire les pixels, n'ajoute aucun detail"),
        new("1440p", 1440, string.Empty),
        new("1080p — Full HD", 1080, "Le plus courant"),
        new("720p — HD", 720, "Fichier nettement plus leger"),
        new("480p", 480, "Pour partager rapidement"),
        new("Personnalisee", null, "Largeur et hauteur libres"),
    ];

    public override string ToString() => Label;
}

public sealed record FrameRateChoice(string Label, double? Value)
{
    public bool IsCustom => Value is null && Label.StartsWith("Personnalis", StringComparison.Ordinal);

    public static IReadOnlyList<FrameRateChoice> All { get; } =
    [
        new("Conserver la source", null),
        new("24 im/s — cinema", 24d),
        new("25 im/s — PAL", 25d),
        new("30 im/s", 30d),
        new("50 im/s", 50d),
        new("60 im/s — fluide", 60d),
        new("120 im/s — tres fluide", 120d),
        new("240 im/s", 240d),
        new("Personnalisee", null),
    ];

    public override string ToString() => Label;
}

/// <summary>Methode employee pour atteindre la cadence demandee.</summary>
public sealed record FrameRateModeChoice(string Label, FrameRateMode Value, string Hint)
{
    public static IReadOnlyList<FrameRateModeChoice> All { get; } =
    [
        new(
            "Dupliquer les images",
            FrameRateMode.Duplicate,
            "Instantane. Le fichier annonce la nouvelle cadence, mais rien n'est plus fluide : chaque image est simplement repetee."),
        new(
            "Calculer les images manquantes",
            FrameRateMode.Interpolate,
            "Fluidite reelle : les images intermediaires sont fabriquees par analyse du mouvement. Beaucoup plus lent, et des artefacts sont possibles sur les mouvements rapides."),
    ];

    public override string ToString() => Label;
}

public sealed record DitherChoice(string Label, DitherMode Value)
{
    public static IReadOnlyList<DitherChoice> All { get; } =
    [
        new("Bayer — fichier compact", DitherMode.Bayer),
        new("Floyd-Steinberg — plus fidele", DitherMode.FloydSteinberg),
        new("Sierra", DitherMode.Sierra2),
        new("Aucun", DitherMode.None),
    ];

    public override string ToString() => Label;
}

public sealed record ScalingChoice(string Label, ScalingAlgorithm Value, string Hint)
{
    public static IReadOnlyList<ScalingChoice> All { get; } =
    [
        new("Lanczos", ScalingAlgorithm.Lanczos, "Le plus net, choix par defaut"),
        new("Bicubique", ScalingAlgorithm.Bicubic, "Plus doux"),
        new("Bilineaire", ScalingAlgorithm.Bilinear, "Rapide, moins precis"),
        new("Spline", ScalingAlgorithm.Spline, "Doux, sans halo"),
        new("Plus proche voisin", ScalingAlgorithm.Neighbor, "Conserve les bords francs : pixel art"),
    ];

    public override string ToString() => Label;
}

public sealed record AudioBitrateChoice(string Label, long? Value)
{
    public static IReadOnlyList<AudioBitrateChoice> All { get; } =
    [
        new("Automatique", null),
        new("96 kb/s — voix", 96_000L),
        new("128 kb/s — correct", 128_000L),
        new("192 kb/s — bon", 192_000L),
        new("256 kb/s — tres bon", 256_000L),
        new("320 kb/s — maximum", 320_000L),
    ];

    public override string ToString() => Label;
}

public sealed record ChannelChoice(string Label, int? Value)
{
    public static IReadOnlyList<ChannelChoice> All { get; } =
    [
        new("Conserver la source", null),
        new("Mono", 1),
        new("Stereo", 2),
        new("5.1", 6),
    ];

    public override string ToString() => Label;
}
