namespace ZiaConvert.Core.Options;

/// <summary>Reglages de conversion de documents bureautiques vers PDF.</summary>
public sealed record DocumentOptions : ConversionOptions
{
    /// <summary>Produit un PDF/A-2b, adapte a l'archivage long terme.</summary>
    public bool ArchiveFormat { get; init; }

    /// <summary>Exporte la table des matieres en signets PDF.</summary>
    public bool ExportBookmarks { get; init; } = true;

    /// <summary>Reechantillonne les images integrees a cette resolution. <c>null</c> les conserve telles quelles.</summary>
    public int? ImageDpi { get; init; }

    /// <summary>Qualite JPEG des images integrees (1-100).</summary>
    public int ImageQuality { get; init; } = 90;

    /// <summary>Pages a exporter, au format LibreOffice (ex. <c>1-5,8</c>). <c>null</c> exporte tout.</summary>
    public string? PageRange { get; init; }
}
