using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Abstractions;

/// <summary>
/// Contrat unique de tous les moteurs de conversion. C'est la seule abstraction que le
/// reste de l'application connait : ajouter un moteur ne demande aucune modification
/// ailleurs, il suffit de l'enregistrer aupres du routeur.
/// </summary>
/// <remarks>
/// Convention d'erreur : un moteur signale un echec en levant une
/// <see cref="ConversionException" />. Le flux de progression ne porte que des etats de
/// succes ; la couche job construit le <see cref="ConversionResult" /> a partir de l'issue.
/// </remarks>
public interface IConversionEngine
{
    /// <summary>Nom court, affiche dans les journaux et l'interface (ex. <c>ffmpeg</c>).</summary>
    string Name { get; }

    /// <summary>Familles que ce moteur declare savoir traiter. Sert au pre-filtrage du routeur.</summary>
    IReadOnlySet<FormatFamily> SupportedFamilies { get; }

    /// <summary>
    /// Verifie que le moteur est utilisable : binaire present, version compatible.
    /// Appele au demarrage et mis en cache ; ne doit pas etre coûteux.
    /// </summary>
    EngineAvailability CheckAvailability();

    /// <summary>Indique si ce moteur sait traiter cette demande precise.</summary>
    bool CanHandle(ConversionRequest request);

    /// <summary>
    /// Execute la conversion en emettant sa progression au fil de l'eau.
    /// </summary>
    /// <remarks>
    /// L'annulation doit etre propre : arret gracieux du processus externe puis suppression
    /// de la sortie partielle. Voir <see cref="ConversionRequest.WorkingPath" />.
    /// </remarks>
    /// <exception cref="ConversionException">La conversion a echoue.</exception>
    /// <exception cref="OperationCanceledException">L'annulation a ete demandee.</exception>
    IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Etat de disponibilite d'un moteur.</summary>
public sealed record EngineAvailability(bool IsAvailable, string? Version = null, string? Reason = null)
{
    public static EngineAvailability Available(string? version = null) => new(true, version);

    /// <param name="reason">Message affichable a l'utilisateur, expliquant quoi installer.</param>
    public static EngineAvailability Missing(string reason) => new(false, Reason: reason);
}
