namespace ZiaConvert.Core.Options;

/// <summary>Reglages de conversion d'image, RAW compris.</summary>
public sealed record ImageOptions : ConversionOptions
{
    /// <summary>Qualite 1-100 pour les formats avec perte. Ignore si <see cref="Lossless" />.</summary>
    public int Quality { get; init; } = 90;

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>Conserve le ratio lors du redimensionnement.</summary>
    public bool PreserveAspectRatio { get; init; } = true;

    /// <summary>Conserve les donnees EXIF/IPTC/XMP de la source.</summary>
    public bool PreserveMetadata { get; init; } = true;

    /// <summary>Applique la rotation indiquee par l'EXIF puis efface le tag d'orientation.</summary>
    public bool AutoOrient { get; init; } = true;

    /// <summary>Espace colorimetrique de sortie. sRGB est le choix sur pour un usage general.</summary>
    public string ColorSpace { get; init; } = "sRGB";

    /// <summary>Encodage sans perte quand le format cible le permet (webp, avif, png).</summary>
    public bool Lossless { get; init; }

    /// <summary>Balance des blancs appliquee au dematricage RAW.</summary>
    public RawWhiteBalance WhiteBalance { get; init; } = RawWhiteBalance.AsShot;
}

public enum RawWhiteBalance
{
    /// <summary>Celle enregistree par le boitier au declenchement.</summary>
    AsShot,

    /// <summary>Calculee sur l'ensemble de l'image.</summary>
    Auto,

    /// <summary>Reference du fabricant.</summary>
    Camera,
}
