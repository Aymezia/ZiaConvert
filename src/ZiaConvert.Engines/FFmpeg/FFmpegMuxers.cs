namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Traduit un format du catalogue vers le nom de multiplexeur attendu par <c>ffmpeg -f</c>.
/// </summary>
/// <remarks>
/// Indispensable ici : toute conversion ecrit d'abord dans un fichier <c>.part</c>, dont
/// l'extension ne veut rien dire pour ffmpeg. Sans <c>-f</c> explicite, il ne saurait pas
/// quel conteneur produire et echouerait avant meme de commencer.
/// </remarks>
internal static class FFmpegMuxers
{
    private static readonly Dictionary<string, string> Muxers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        ["mp4"] = "mp4",
        ["m4v"] = "mp4",
        ["mkv"] = "matroska",
        ["webm"] = "webm",
        ["mov"] = "mov",
        ["avi"] = "avi",
        ["ts"] = "mpegts",
        ["mpg"] = "mpeg",
        ["flv"] = "flv",
        ["3gp"] = "3gp",
        ["ogv"] = "ogg",
        ["wmv"] = "asf",

        // Audio
        ["mp3"] = "mp3",
        ["aac"] = "adts",
        ["m4a"] = "ipod",
        ["flac"] = "flac",
        ["wav"] = "wav",
        ["opus"] = "opus",
        ["ogg"] = "ogg",
        ["wma"] = "asf",
        ["aiff"] = "aiff",

        // Image animee
        ["gif"] = "gif",
    };

    public static string? For(string formatId) => Muxers.GetValueOrDefault(formatId);
}
