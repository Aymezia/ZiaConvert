namespace ZiaConvert.Core.Updates;

/// <summary>Nouvelle version disponible, telle que rapportee par la derniere release GitHub.</summary>
public sealed record UpdateInfo
{
    public required string Version { get; init; }

    /// <summary>Lien direct vers l'installateur (.exe) attache a la release.</summary>
    public required string InstallerUrl { get; init; }

    public required string ReleaseUrl { get; init; }
}
