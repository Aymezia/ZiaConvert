using ZiaConvert.Core.Options;

namespace ZiaConvert.App.ViewModels;

/// <summary>
/// Point de depart pour les reglages.
/// </summary>
/// <remarks>
/// Un preglage ne masque rien : il remplit les champs du panneau, qui restent tous
/// modifiables ensuite. C'est preferable a un mode « personnalise » separe, ou l'on ne
/// sait jamais quelles valeurs sont reellement appliquees.
/// </remarks>
public sealed record ConversionPreset
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public VideoCodec Codec { get; init; } = VideoCodec.Auto;

    public int Quality { get; init; } = 23;

    /// <summary>Hauteur cible. <c>null</c> conserve la resolution de la source.</summary>
    public int? Height { get; init; }

    public AudioCodec Audio { get; init; } = AudioCodec.Auto;

    public long? AudioBitrate { get; init; }

    public bool AllowRemux { get; init; }

    public static IReadOnlyList<ConversionPreset> All { get; } =
    [
        new()
        {
            Name = "Rapide",
            Description = "Change de conteneur sans reencoder quand c'est possible. Quelques secondes, aucune perte.",
            AllowRemux = true,
        },
        new()
        {
            Name = "Web et partage",
            Description = "1080p, H.264, lisible partout. Le bon compromis pour envoyer ou publier.",
            Codec = VideoCodec.H264,
            Quality = 23,
            Height = 1080,
            Audio = AudioCodec.Aac,
            AudioBitrate = 128_000L,
        },
        new()
        {
            Name = "Fichier leger",
            Description = "720p en H.265. Reduit fortement la taille, au prix de details dans les scenes complexes.",
            Codec = VideoCodec.H265,
            Quality = 28,
            Height = 720,
            Audio = AudioCodec.Aac,
            AudioBitrate = 96_000L,
        },
        new()
        {
            Name = "Qualite",
            Description = "H.265 a haute qualite, resolution d'origine conservee.",
            Codec = VideoCodec.H265,
            Quality = 20,
        },
        new()
        {
            Name = "Archivage",
            Description = "H.265 quasiment sans perte. Fichier volumineux, conversion lente.",
            Codec = VideoCodec.H265,
            Quality = 16,
        },
    ];

    public override string ToString() => Name;
}
