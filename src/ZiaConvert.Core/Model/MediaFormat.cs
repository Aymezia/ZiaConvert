namespace ZiaConvert.Core.Model;

/// <summary>
/// Description d'un format de fichier supporte. Les instances sont creees une seule
/// fois par <see cref="Routing.FormatRegistry" /> et partagees.
/// </summary>
public sealed record MediaFormat
{
    /// <summary>Identifiant stable, en minuscules, sans point (ex. <c>mp4</c>).</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required FormatFamily Family { get; init; }

    /// <summary>Extensions associees, point inclus et en minuscules. La premiere est celle utilisee en sortie.</summary>
    public required IReadOnlyList<string> Extensions { get; init; }

    public string? MimeType { get; init; }

    /// <summary>Faux pour les formats qu'on sait seulement produire (ex. un rapport).</summary>
    public bool CanBeSource { get; init; } = true;

    /// <summary>Faux pour les formats qu'on sait seulement lire (ex. les RAW : on ne fabrique pas de CR2).</summary>
    public bool CanBeTarget { get; init; } = true;

    public string PrimaryExtension => Extensions[0];

    public override string ToString() => Id;
}
