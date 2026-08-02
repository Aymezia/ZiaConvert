namespace ZiaConvert.Core.Model;

/// <summary>Issue d'une conversion, construite par la couche job a partir du flux de progression.</summary>
public sealed record ConversionResult
{
    public required bool Success { get; init; }

    public required string OutputPath { get; init; }

    public required string EngineName { get; init; }

    public long OutputSizeBytes { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Ce que le moteur a reellement fait, dans ses propres termes — par exemple
    /// « Copie des flux sans reencodage » ou « Reencodage h264_nvenc (materiel) ».
    /// Permet d'expliquer a l'utilisateur pourquoi une conversion a pris deux secondes
    /// ou dix minutes.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Signale une sortie suspecte malgre un moteur qui s'est termine sans erreur — par
    /// exemple une duree tres inferieure a la source, indice d'un fichier tronque. La
    /// conversion reste un succes (le fichier existe et le moteur n'a rien signale) :
    /// c'est un avertissement a afficher, pas un echec a provoquer.
    /// </summary>
    public string? VerificationWarning { get; init; }

    public string? ErrorMessage { get; init; }

    public static ConversionResult Ok(
        string outputPath,
        string engineName,
        TimeSpan duration,
        string? detail = null,
        string? verificationWarning = null) => new()
        {
            Success = true,
            OutputPath = outputPath,
            EngineName = engineName,
            Duration = duration,
            Detail = detail,
            VerificationWarning = verificationWarning,
            OutputSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0L,
        };

    public static ConversionResult Failed(string outputPath, string engineName, string error) => new()
    {
        Success = false,
        OutputPath = outputPath,
        EngineName = engineName,
        ErrorMessage = error,
    };
}
