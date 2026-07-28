namespace ZiaConvert.Core.Options;

/// <summary>
/// Reglages d'un agrandissement par reseau de neurones. A ne pas confondre avec le
/// redimensionnement de <see cref="ImageOptions" /> : celui-ci reconstruit du detail,
/// mais coute plusieurs secondes par image.
/// </summary>
public sealed record UpscaleOptions : ConversionOptions
{
    /// <summary>Facteur d'agrandissement : 2, 3 ou 4 selon le modele.</summary>
    public int Factor { get; init; } = 4;

    /// <summary>Nom du modele ncnn (ex. <c>realesrgan-x4plus</c>, <c>realesrgan-x4plus-anime</c>).</summary>
    public string Model { get; init; } = "realesrgan-x4plus";

    /// <summary>Taille des tuiles de traitement. Plus petit consomme moins de VRAM. 0 = automatique.</summary>
    public int TileSize { get; init; }

    /// <summary>Index du GPU a utiliser. <c>null</c> laisse le moteur choisir.</summary>
    public int? GpuId { get; init; }
}
