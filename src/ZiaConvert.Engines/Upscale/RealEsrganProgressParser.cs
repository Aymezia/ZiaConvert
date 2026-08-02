using System.Globalization;

namespace ZiaConvert.Engines.Upscale;

/// <summary>
/// Lit les lignes de progression de <c>realesrgan-ncnn-vulkan -v</c>.
/// </summary>
/// <remarks>
/// L'outil emet un pourcentage par tuile traitee (ex. <c>25,00%</c>), sur la sortie
/// standard. Le separateur decimal suit la locale systeme du processus plutot que la
/// culture invariante — confirme empiriquement sur une machine en fr-FR, ou il produit
/// une virgule et non un point. On normalise donc systematiquement avant de parser.
/// </remarks>
internal static class RealEsrganProgressParser
{
    /// <returns>Le pourcentage (0-100), ou <c>null</c> si la ligne n'en est pas une.</returns>
    public static double? TryParse(string line)
    {
        var trimmed = line.Trim();

        if (!trimmed.EndsWith('%'))
        {
            return null;
        }

        var normalized = trimmed[..^1].Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0d, 100d)
            : null;
    }
}
