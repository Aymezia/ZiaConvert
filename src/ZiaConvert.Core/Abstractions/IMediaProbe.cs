using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Abstractions;

/// <summary>
/// Lit les metadonnees d'un fichier source. Le routeur s'en sert pour decider entre
/// remux et transcodage, et pour connaitre la duree — indispensable au calcul du pourcentage.
/// </summary>
public interface IMediaProbe
{
    /// <exception cref="ConversionException">Le fichier est illisible ou dans un format non reconnu.</exception>
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default);
}
