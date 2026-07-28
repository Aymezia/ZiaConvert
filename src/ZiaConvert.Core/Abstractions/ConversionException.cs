namespace ZiaConvert.Core.Abstractions;

/// <summary>
/// Echec d'une conversion. Le message doit rester affichable tel quel a l'utilisateur ;
/// les details techniques vont dans <see cref="EngineOutput" />.
/// </summary>
public class ConversionException : Exception
{
    public ConversionException(string message)
        : base(message)
    {
    }

    public ConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConversionException(string message, string? engineName, IReadOnlyList<string>? engineOutput = null)
        : base(message)
    {
        EngineName = engineName;
        EngineOutput = engineOutput ?? [];
    }

    public string? EngineName { get; init; }

    /// <summary>Dernieres lignes emises par le moteur : c'est la que se trouve la cause reelle.</summary>
    public IReadOnlyList<string> EngineOutput { get; init; } = [];
}

/// <summary>Aucun moteur disponible ne sait traiter la conversion demandee.</summary>
public sealed class UnsupportedConversionException : ConversionException
{
    public UnsupportedConversionException(string message)
        : base(message)
    {
    }
}
