namespace ZiaConvert.Core.Model;

/// <summary>
/// Un point d'avancement emis par un moteur pendant une conversion.
/// Les moteurs en produisent un flux via <see cref="Abstractions.IConversionEngine.ExecuteAsync" />.
/// </summary>
public sealed record ConversionProgress
{
    /// <summary>Avancement de 0 a 100, ou <c>null</c> quand le moteur ne sait pas l'estimer.</summary>
    public double? Percent { get; init; }

    public ConversionStage Stage { get; init; } = ConversionStage.Running;

    public TimeSpan? Elapsed { get; init; }

    public TimeSpan? Eta { get; init; }

    /// <summary>Vitesse relative au temps reel : <c>2.5</c> signifie 2,5x plus rapide que la lecture.</summary>
    public double? Speed { get; init; }

    public long? OutputBytes { get; init; }

    /// <summary>Message court destine a l'interface (ex. « Generation de la palette »).</summary>
    public string? Message { get; init; }

    public static ConversionProgress At(double percent, ConversionStage stage = ConversionStage.Running) =>
        new() { Percent = Math.Clamp(percent, 0d, 100d), Stage = stage };

    public static ConversionProgress Indeterminate(ConversionStage stage, string? message = null) =>
        new() { Stage = stage, Message = message };

    public static ConversionProgress Done() =>
        new() { Percent = 100d, Stage = ConversionStage.Completed };
}

public enum ConversionStage
{
    /// <summary>Sonde du fichier source, decision remux/transcode.</summary>
    Analyzing,

    Running,

    /// <summary>Travail termine, ecriture finale en cours (renommage du .part, nettoyage).</summary>
    Finalizing,

    Completed,
}
