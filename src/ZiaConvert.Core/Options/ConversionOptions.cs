namespace ZiaConvert.Core.Options;

/// <summary>
/// Base des reglages de conversion. Chaque famille a son propre enregistrement derive :
/// le moteur teste le type concret et ignore ce qui ne le concerne pas.
/// </summary>
public abstract record ConversionOptions
{
    /// <summary>Reglages vides : le moteur applique ses valeurs par defaut.</summary>
    public static ConversionOptions None { get; } = new DefaultOptions();

    private sealed record DefaultOptions : ConversionOptions;
}

public enum VideoCodec { Auto, Copy, H264, H265, Av1, Vp9, ProRes }

public enum AudioCodec { Auto, Copy, Aac, Mp3, Opus, Flac, Vorbis, Pcm, None }

/// <summary>Encodeur materiel demande. <see cref="Auto" /> laisse le detecteur choisir avec repli logiciel.</summary>
public enum HardwareAcceleration { Auto, None, Nvenc, QuickSync, Amf, VideoToolbox }

public enum ScalingAlgorithm { Lanczos, Bicubic, Bilinear, Neighbor, Spline }

public enum DitherMode { None, Bayer, FloydSteinberg, Sierra2 }

/// <summary>Facon d'atteindre la cadence demandee.</summary>
public enum FrameRateMode
{
    /// <summary>
    /// Duplique ou supprime des images. Instantane, mais passer de 60 a 120 ainsi
    /// n'apporte aucune fluidite : chaque image est simplement affichee deux fois.
    /// Le fichier annonce 120 im/s sans rien contenir de plus.
    /// </summary>
    Duplicate,

    /// <summary>
    /// Fabrique les images manquantes par estimation de mouvement. C'est la seule
    /// facon d'obtenir une reelle fluidite, au prix d'un calcul tres lent et
    /// d'artefacts possibles sur les mouvements rapides ou les changements de plan.
    /// </summary>
    Interpolate,
}
