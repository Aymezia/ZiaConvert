using System.Text.Json;
using ZiaConvert.App.ViewModels;

namespace ZiaConvert.App;

/// <summary>
/// Charge et sauvegarde les preglages crees par l'utilisateur.
/// </summary>
/// <remarks>
/// Seuls les preglages personnalises sont ecrits sur disque : les preglages fixes
/// (<see cref="ConversionPreset.All" />) sont deja dans le code, les persister aurait
/// double l'information pour rien.
/// </remarks>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZiaConvert",
        "presets.json");

    /// <summary>Preglages personnalises enregistres, dans l'ordre ou ils ont ete crees.</summary>
    public IReadOnlyList<ConversionPreset> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<ConversionPreset>>(File.ReadAllText(_path), JsonOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un fichier illisible ne doit pas empecher l'application de demarrer : on
            // repart simplement sans preglages personnalises.
            return [];
        }
    }

    public void Save(IReadOnlyList<ConversionPreset> customPresets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(customPresets, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le preglage reste utilisable pour la session en cours, simplement pas
            // retrouve au prochain lancement.
        }
    }
}
