using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Jobs;

/// <summary>
/// Nombre de conversions simultanees autorisees, par famille de format.
/// </summary>
/// <remarks>
/// Le parallelisme se regle par famille et non globalement, parce que les moteurs n'ont
/// pas du tout le meme profil. Un encodage video sature deja tous les cœurs a lui seul :
/// en lancer dix en parallele ne va pas plus vite et rend l'interface inutilisable.
/// Une conversion d'image, elle, passe l'essentiel de son temps sur le disque.
/// </remarks>
public sealed record ConcurrencyPolicy
{
    /// <summary>Les encodeurs video sont deja multi-thread : au-dela de deux, on se marche dessus.</summary>
    public int Video { get; init; } = 2;

    public int Audio { get; init; } = 3;

    public int Image { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    /// <summary>
    /// Obligatoirement 1 : LibreOffice refuse deux instances partageant le meme profil,
    /// et sequentialiser est plus sur que de jongler avec des profils temporaires.
    /// </summary>
    public int Document { get; init; } = 1;

    public static ConcurrencyPolicy Default { get; } = new();

    /// <summary>Nombre de workers a demarrer pour honorer la limite la plus haute.</summary>
    public int MaxWorkers => Math.Max(Math.Max(Video, Audio), Math.Max(Image, Document));

    public int For(FormatFamily family) => family switch
    {
        FormatFamily.Video => Video,
        FormatFamily.Audio => Audio,
        FormatFamily.Image or FormatFamily.RawImage => Image,
        FormatFamily.Document => Document,
        _ => 1,
    };
}
