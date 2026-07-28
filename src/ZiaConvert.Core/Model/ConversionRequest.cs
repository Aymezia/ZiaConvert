using ZiaConvert.Core.Options;

namespace ZiaConvert.Core.Model;

/// <summary>
/// Une conversion a effectuer. Immuable : c'est l'unite de travail passee au routeur
/// puis au moteur retenu.
/// </summary>
public sealed record ConversionRequest
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public required MediaFormat SourceFormat { get; init; }

    public required MediaFormat TargetFormat { get; init; }

    public ConversionOptions Options { get; init; } = ConversionOptions.None;

    /// <summary>Ecrase la sortie si elle existe deja. Sinon la conversion echoue avant de demarrer.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// Metadonnees du fichier source. Renseignees par le routeur lors de l'analyse et
    /// reutilisees par le moteur, pour ne pas sonder le fichier deux fois.
    /// </summary>
    public MediaInfo? SourceInfo { get; init; }

    /// <summary>
    /// Chemin de travail utilise pendant la conversion. On ecrit toujours dans un fichier
    /// temporaire renomme a la reussite : une annulation ne laisse jamais de sortie corrompue.
    /// </summary>
    public string WorkingPath => OutputPath + ".part";
}
