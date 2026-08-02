namespace ZiaConvert.Core.Options;

/// <summary>Piste de sous-titres externe (fichier texte) a integrer a la sortie.</summary>
public sealed record SubtitleImport
{
    public required string FilePath { get; init; }

    /// <summary>Code langue affiche par le lecteur (ex. « fre », « eng »). Facultatif.</summary>
    public string? Language { get; init; }

    /// <summary>Nom de la piste affiche par le lecteur (ex. « VOSTFR »). Facultatif.</summary>
    public string? Title { get; init; }
}
