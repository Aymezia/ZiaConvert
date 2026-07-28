namespace ZiaConvert.Core.Abstractions;

/// <summary>
/// Resout le chemin absolu des binaires externes. Isole les moteurs de la question
/// « ou est installe l'outil », qui differe selon qu'il est embarque dans l'application,
/// telecharge a la demande, ou deja present sur la machine.
/// </summary>
public interface IEngineLocator
{
    /// <summary>
    /// Cherche un outil par son nom sans extension (ex. <c>ffmpeg</c>).
    /// </summary>
    /// <returns>Le chemin absolu, ou <c>null</c> si l'outil est introuvable.</returns>
    string? Locate(string toolName);
}
