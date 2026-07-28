namespace ZiaConvert.Core.Model;

/// <summary>
/// Grande famille d'un format. C'est le premier critere de routage : il determine
/// quel moteur est susceptible de traiter un fichier.
/// </summary>
public enum FormatFamily
{
    Unknown = 0,
    Video,
    Audio,
    Image,

    /// <summary>
    /// Negatifs numeriques (CR2, NEF, ARW...). Separes des images classiques car ils
    /// exigent un dematricage et une gestion de profil couleur specifiques.
    /// </summary>
    RawImage,

    Document,
}
