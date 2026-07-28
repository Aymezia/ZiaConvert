namespace ZiaConvert.Core.Processes;

/// <summary>Un processus externe s'est termine avec un code de sortie non nul.</summary>
public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string fileName, int exitCode, IReadOnlyList<string> errorTail)
        : base(BuildMessage(fileName, exitCode, errorTail))
    {
        FileName = fileName;
        ExitCode = exitCode;
        ErrorTail = errorTail;
    }

    public string FileName { get; }

    public int ExitCode { get; }

    /// <summary>Dernieres lignes de la sortie d'erreur : la cause reelle s'y trouve presque toujours.</summary>
    public IReadOnlyList<string> ErrorTail { get; }

    private static string BuildMessage(string fileName, int exitCode, IReadOnlyList<string> errorTail)
    {
        var name = Path.GetFileName(fileName);
        var detail = errorTail.Count > 0
            ? Environment.NewLine + string.Join(Environment.NewLine, errorTail)
            : string.Empty;

        return $"{name} s'est termine avec le code {exitCode}.{detail}";
    }
}
