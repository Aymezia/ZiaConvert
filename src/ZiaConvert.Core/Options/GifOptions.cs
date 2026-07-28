namespace ZiaConvert.Core.Options;

/// <summary>
/// Reglages de la conversion video vers GIF. Traitee a part car elle passe par une
/// generation de palette : les reglages video classiques n'ont pas de sens ici.
/// </summary>
public sealed record GifOptions : ConversionOptions
{
    /// <summary>Images par seconde du GIF. Au-dela de 20 le fichier grossit vite pour un gain faible.</summary>
    public double FrameRate { get; init; } = 15d;

    /// <summary>Largeur cible ; la hauteur suit le ratio. <c>null</c> conserve la source.</summary>
    public int? Width { get; init; } = 480;

    public DitherMode Dither { get; init; } = DitherMode.Bayer;

    /// <summary>Force du motif de Bayer (1-5). Plus bas = motif plus visible mais fichier plus petit.</summary>
    public int BayerScale { get; init; } = 3;

    /// <summary>
    /// Calcule la palette sur les differences entre images plutot que sur l'ensemble.
    /// Meilleur rendu sur les plans a fond fixe.
    /// </summary>
    public bool DiffPalette { get; init; } = true;

    public bool Loop { get; init; } = true;

    public TimeSpan? StartTime { get; init; }

    public TimeSpan? EndTime { get; init; }
}
