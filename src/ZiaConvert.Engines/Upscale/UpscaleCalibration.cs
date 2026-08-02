using System.Text.Json.Serialization;

namespace ZiaConvert.Engines.Upscale;

/// <summary>
/// Modele lineaire du temps de traitement pour un couple (modele, facteur) donne :
/// <c>duree = FixedOverheadSeconds + pixels_de_sortie / 1_000_000 * SecondsPerMegapixel</c>.
/// </summary>
/// <remarks>
/// Mesure empiriquement sur une RTX 4060 Ti : environ 90% du temps d'un petit agrandissement
/// est un cout fixe (demarrage du processus, chargement du modele, compilation des shaders
/// Vulkan — en grande partie mise en cache par le pilote des la deuxieme execution), le
/// reste croit avec le nombre de pixels produits. Un seul point de mesure surestimerait
/// grossierement les grandes images ; deux points suffisent a separer les deux termes.
/// </remarks>
internal sealed record UpscaleCalibration
{
    [JsonPropertyName("fixedOverheadSeconds")]
    public double FixedOverheadSeconds { get; init; }

    [JsonPropertyName("secondsPerMegapixel")]
    public double SecondsPerMegapixel { get; init; }

    public TimeSpan Estimate(long outputPixels)
    {
        var megapixels = outputPixels / 1_000_000d;
        var seconds = FixedOverheadSeconds + (megapixels * SecondsPerMegapixel);

        // Le modele lineaire peut descendre sous zero par bruit de mesure sur de tres
        // petites images : la duree reelle ne peut jamais etre negative.
        return TimeSpan.FromSeconds(Math.Max(seconds, 0.1d));
    }
}

internal sealed record UpscaleBenchmarkCache
{
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("calibrations")]
    public Dictionary<string, UpscaleCalibration> Calibrations { get; init; } = [];
}
