using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;

namespace ZiaConvert.Core.Routing;

/// <summary>
/// Aiguille une demande de conversion vers le moteur capable de la traiter, et prepare
/// le terrain en analysant la source au prealable.
/// </summary>
/// <remarks>
/// L'analyse est faite ici, une fois pour toutes, plutot que dans chaque moteur : elle
/// coute un lancement de processus, et son resultat sert aussi bien a decider d'un remux
/// qu'a calculer un pourcentage d'avancement.
/// </remarks>
public sealed class ConversionRouter
{
    private readonly IReadOnlyList<IConversionEngine> _engines;
    private readonly IMediaProbe? _mediaProbe;
    private readonly FormatRegistry _formats;
    private readonly ILogger _logger;

    public ConversionRouter(
        IEnumerable<IConversionEngine> engines,
        IMediaProbe? mediaProbe = null,
        FormatRegistry? formats = null,
        ILogger<ConversionRouter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engines);

        _engines = [.. engines];
        _mediaProbe = mediaProbe;
        _formats = formats ?? FormatRegistry.Default;
        _logger = logger ?? NullLogger<ConversionRouter>.Instance;
    }

    /// <summary>
    /// Compose une demande a partir de deux chemins, en deduisant les formats des extensions.
    /// </summary>
    /// <exception cref="UnsupportedConversionException">Une des deux extensions est inconnue.</exception>
    public ConversionRequest CreateRequest(
        string inputPath,
        string outputPath,
        ConversionOptions? options = null,
        bool overwrite = false) => new()
        {
            InputPath = Path.GetFullPath(inputPath),
            OutputPath = Path.GetFullPath(outputPath),
            SourceFormat = _formats.GetByPath(inputPath),
            TargetFormat = _formats.GetByPath(outputPath),
            Options = options ?? ConversionOptions.None,
            Overwrite = overwrite,
        };

    /// <summary>
    /// Analyse la source quand c'est pertinent et rend la demande enrichie.
    /// </summary>
    public async Task<ConversionRequest> PrepareAsync(
        ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceInfo is not null || _mediaProbe is null)
        {
            return request;
        }

        if (request.SourceFormat.Family is not (FormatFamily.Video or FormatFamily.Audio))
        {
            return request;
        }

        try
        {
            var info = await _mediaProbe.ProbeAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
            return request with { SourceInfo = info };
        }
        catch (ConversionException ex)
        {
            // Une sonde en echec n'empeche pas de convertir : on perd seulement le remux
            // automatique et l'affichage d'un pourcentage. Le moteur dira si c'est bloquant.
            _logger.LogWarning(ex, "Analyse impossible de {File}, la conversion continue a l'aveugle.", request.InputPath);
            return request;
        }
    }

    /// <summary>Designe le moteur qui traitera la demande.</summary>
    /// <exception cref="UnsupportedConversionException">Aucun moteur disponible ne sait la traiter.</exception>
    public IConversionEngine SelectEngine(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = _engines.Where(e => e.CanHandle(request)).ToList();

        if (candidates.Count == 0)
        {
            throw new UnsupportedConversionException(
                $"Aucun moteur ne sait convertir « {request.SourceFormat.DisplayName} » vers « {request.TargetFormat.DisplayName} ».");
        }

        // Un moteur present mais non installe doit produire un message qui explique quoi
        // faire, pas un « conversion impossible » qui laisserait l'utilisateur sans recours.
        foreach (var candidate in candidates)
        {
            var availability = candidate.CheckAvailability();

            if (availability.IsAvailable)
            {
                return candidate;
            }

            _logger.LogDebug("{Engine} ecarte : {Reason}", candidate.Name, availability.Reason);
        }

        var reasons = candidates
            .Select(c => c.CheckAvailability().Reason)
            .Where(r => r is not null);

        throw new UnsupportedConversionException(
            $"Le moteur necessaire n'est pas disponible. {string.Join(" ", reasons)}");
    }

    /// <summary>Formats de sortie proposables pour un fichier donne.</summary>
    public IEnumerable<MediaFormat> TargetsFor(string inputPath) =>
        _formats.FindByPath(inputPath) is { } format
            ? _formats.TargetsFor(format)
            : [];
}
