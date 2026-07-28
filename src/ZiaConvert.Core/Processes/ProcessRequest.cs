namespace ZiaConvert.Core.Processes;

/// <summary>Description d'un processus externe a lancer.</summary>
public sealed record ProcessRequest
{
    /// <summary>Chemin de l'executable.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Arguments un par un, sans guillemets ni echappement manuel : ils sont passes via
    /// <c>ProcessStartInfo.ArgumentList</c>, qui gere l'echappement de la plateforme.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Sequence ecrite sur l'entree standard pour demander un arret propre a l'annulation.
    /// Pour ffmpeg c'est <c>"q"</c> : il ferme alors correctement le conteneur de sortie.
    /// <c>null</c> passe directement par la terminaison forcee.
    /// </summary>
    public string? GracefulStopInput { get; init; }

    /// <summary>Delai laisse au processus pour s'arreter de lui-meme avant terminaison forcee.</summary>
    public TimeSpan GracefulStopTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Nombre de lignes d'erreur conservees pour le diagnostic en cas d'echec.</summary>
    public int ErrorTailLines { get; init; } = 40;
}

public enum ProcessStreamKind
{
    StandardOutput,
    StandardError,
}

/// <summary>Une ligne emise par un processus, avec le flux dont elle provient.</summary>
public readonly record struct ProcessOutputLine(ProcessStreamKind Stream, string Text)
{
    public bool IsError => Stream == ProcessStreamKind.StandardError;

    public override string ToString() => Text;
}

/// <summary>Issue d'une execution collectee en une fois.</summary>
public sealed record ProcessResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError)
{
    public bool Success => ExitCode == 0;

    public string StandardOutputText => string.Join(Environment.NewLine, StandardOutput);

    public string StandardErrorText => string.Join(Environment.NewLine, StandardError);
}
