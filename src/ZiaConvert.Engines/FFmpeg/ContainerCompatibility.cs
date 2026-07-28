using ZiaConvert.Core.Model;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Sait quels codecs chaque conteneur accepte sans reencodage.
/// </summary>
/// <remarks>
/// C'est la table qui rend possible le remux : convertir un MP4 en MKV ne demande souvent
/// que de reecrire l'enveloppe, ce qui prend deux secondes au lieu de dix minutes et ne
/// perd aucune qualite. La table est volontairement conservatrice — en cas de doute on
/// reencode, ce qui est lent mais toujours correct, plutot que de produire un fichier
/// que le lecteur de l'utilisateur refusera d'ouvrir.
/// </remarks>
internal static class ContainerCompatibility
{
    /// <summary>Conteneurs qui acceptent a peu pres tout : inutile d'y lister les codecs un par un.</summary>
    private static readonly HashSet<string> Universal = new(StringComparer.OrdinalIgnoreCase)
    {
        "mkv",
    };

    private static readonly Dictionary<string, ContainerCodecs> Containers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mp4"] = new(
                Video: ["h264", "hevc", "mpeg4", "av1", "vp9", "mjpeg"],
                Audio: ["aac", "mp3", "ac3", "eac3", "alac", "opus"]),

            ["m4v"] = new(
                Video: ["h264", "hevc", "mpeg4"],
                Audio: ["aac", "ac3", "alac"]),

            ["mov"] = new(
                Video: ["h264", "hevc", "prores", "mpeg4", "mjpeg", "dnxhd"],
                Audio: ["aac", "alac", "pcm_s16le", "pcm_s24le", "mp3"]),

            ["webm"] = new(
                Video: ["vp8", "vp9", "av1"],
                Audio: ["vorbis", "opus"]),

            ["avi"] = new(
                Video: ["mpeg4", "mjpeg", "msmpeg4v3", "h264"],
                Audio: ["mp3", "ac3", "pcm_s16le"]),

            ["ts"] = new(
                Video: ["h264", "hevc", "mpeg2video"],
                Audio: ["aac", "mp3", "ac3", "eac3"]),

            ["mpg"] = new(
                Video: ["mpeg1video", "mpeg2video"],
                Audio: ["mp2", "mp3"]),

            ["flv"] = new(
                Video: ["h264", "flv1"],
                Audio: ["aac", "mp3"]),

            ["3gp"] = new(
                Video: ["h264", "mpeg4", "h263"],
                Audio: ["aac", "amr_nb"]),

            ["ogv"] = new(
                Video: ["theora", "vp8"],
                Audio: ["vorbis", "opus", "flac"]),

            ["wmv"] = new(
                Video: ["wmv1", "wmv2", "wmv3", "msmpeg4v3"],
                Audio: ["wmav1", "wmav2"]),

            // --- Conteneurs audio seuls -------------------------------------------------
            ["mp3"] = new(Video: [], Audio: ["mp3"]),
            ["aac"] = new(Video: [], Audio: ["aac"]),
            ["m4a"] = new(Video: [], Audio: ["aac", "alac"]),
            ["flac"] = new(Video: [], Audio: ["flac"]),
            ["opus"] = new(Video: [], Audio: ["opus"]),
            ["ogg"] = new(Video: [], Audio: ["vorbis", "opus", "flac"]),
            ["wma"] = new(Video: [], Audio: ["wmav1", "wmav2"]),
            ["wav"] = new(Video: [], Audio: ["pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le"]),
            ["aiff"] = new(Video: [], Audio: ["pcm_s16be", "pcm_s24be"]),
        };

    public static bool AcceptsVideoCodec(string containerId, string codecName) =>
        Universal.Contains(containerId) ||
        (Containers.TryGetValue(containerId, out var codecs) &&
         codecs.Video.Contains(codecName, StringComparer.OrdinalIgnoreCase));

    public static bool AcceptsAudioCodec(string containerId, string codecName) =>
        Universal.Contains(containerId) ||
        (Containers.TryGetValue(containerId, out var codecs) &&
         codecs.Audio.Contains(codecName, StringComparer.OrdinalIgnoreCase));

    /// <summary>Vrai si le conteneur peut porter une piste video.</summary>
    public static bool SupportsVideo(string containerId) =>
        Universal.Contains(containerId) ||
        (Containers.TryGetValue(containerId, out var codecs) && codecs.Video.Count > 0);

    /// <summary>
    /// Determine si la conversion peut se faire par simple copie des flux.
    /// </summary>
    /// <param name="reason">
    /// Quand le remux est impossible, explique pourquoi. Sert a justifier a l'utilisateur
    /// pourquoi une conversion « evidente » prend dix minutes.
    /// </param>
    public static bool CanRemux(string targetContainerId, MediaInfo source, out string? reason)
    {
        if (!Containers.ContainsKey(targetContainerId) && !Universal.Contains(targetContainerId))
        {
            reason = $"Le conteneur « {targetContainerId} » n'a pas de regle de compatibilite connue.";
            return false;
        }

        foreach (var stream in source.Streams)
        {
            switch (stream.Kind)
            {
                case MediaStreamKind.Video when !AcceptsVideoCodec(targetContainerId, stream.CodecName):
                    reason = $"{targetContainerId} n'accepte pas la video {stream.CodecName}.";
                    return false;

                case MediaStreamKind.Audio when !AcceptsAudioCodec(targetContainerId, stream.CodecName):
                    reason = $"{targetContainerId} n'accepte pas l'audio {stream.CodecName}.";
                    return false;

                default:
                    break;
            }
        }

        if (source.Streams.Count == 0)
        {
            reason = "Aucun flux exploitable n'a ete detecte dans le fichier source.";
            return false;
        }

        reason = null;
        return true;
    }

    private sealed record ContainerCodecs(IReadOnlyList<string> Video, IReadOnlyList<string> Audio);
}
