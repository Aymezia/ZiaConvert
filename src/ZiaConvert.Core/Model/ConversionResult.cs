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

    public string? ErrorMessage { get; init; }

    public static ConversionResult Ok(
        string outputPath,
        string engineName,
        TimeSpan duration,
        string? detail = null) => new()
        {
            Success = true,
            OutputPath = outputPath,
            EngineName = engineName,
            Duration = duration,
            Detail = detail,
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
