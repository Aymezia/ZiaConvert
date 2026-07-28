using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Routing;

/// <summary>
/// Catalogue des formats connus et des conversions qui ont un sens entre eux.
/// C'est la source de verite pour peupler les listes de l'interface : si un format
/// n'est pas ici, l'application ne le propose pas.
/// </summary>
public sealed class FormatRegistry
{
    private readonly Dictionary<string, MediaFormat> _byId;
    private readonly Dictionary<string, MediaFormat> _byExtension;

    public FormatRegistry(IEnumerable<MediaFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        All = [.. formats];
        _byId = All.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        _byExtension = new Dictionary<string, MediaFormat>(StringComparer.OrdinalIgnoreCase);

        foreach (var format in All)
        {
            foreach (var extension in format.Extensions)
            {
                // Premier arrive, premier servi : une extension partagee revient au format
                // le plus courant, declare en premier dans le catalogue.
                _byExtension.TryAdd(extension, format);
            }
        }
    }

    /// <summary>Catalogue standard de l'application.</summary>
    public static FormatRegistry Default { get; } = new(BuildDefaultFormats());

    public IReadOnlyList<MediaFormat> All { get; }

    public MediaFormat? FindById(string id) =>
        _byId.GetValueOrDefault(id);

    /// <param name="extension">Avec ou sans point, la casse est ignoree.</param>
    public MediaFormat? FindByExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : '.' + extension;
        return _byExtension.GetValueOrDefault(normalized);
    }

    public MediaFormat? FindByPath(string path) =>
        FindByExtension(Path.GetExtension(path));

    /// <exception cref="UnsupportedConversionException">L'extension n'est pas au catalogue.</exception>
    public MediaFormat GetByPath(string path)
    {
        var extension = Path.GetExtension(path);

        return FindByExtension(extension)
            ?? throw new UnsupportedConversionException(
                string.IsNullOrEmpty(extension)
                    ? $"Le fichier « {Path.GetFileName(path)} » n'a pas d'extension : impossible d'en deduire le format."
                    : $"Le format « {extension} » n'est pas pris en charge.");
    }

    public IEnumerable<MediaFormat> ByFamily(FormatFamily family) =>
        All.Where(f => f.Family == family);

    /// <summary>
    /// Formats de sortie proposables pour une source donnee. Volontairement permissif :
    /// c'est au routeur de trancher la faisabilite reelle, cette liste sert a peupler l'interface.
    /// </summary>
    public IEnumerable<MediaFormat> TargetsFor(MediaFormat source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var families = source.Family switch
        {
            // Une video donne une autre video, une image animee, ou sa seule bande son.
            FormatFamily.Video => new[] { FormatFamily.Video, FormatFamily.Audio, FormatFamily.Image },
            FormatFamily.Audio => [FormatFamily.Audio],

            // Un RAW se developpe vers une image classique, jamais vers un autre RAW.
            FormatFamily.Image or FormatFamily.RawImage => [FormatFamily.Image],
            FormatFamily.Document => [FormatFamily.Document],
            _ => [],
        };

        return All.Where(f => f.CanBeTarget && families.Contains(f.Family) && f.Id != source.Id);
    }

    private static MediaFormat[] BuildDefaultFormats() =>
    [
        // --- Video -------------------------------------------------------------------
        Video("mp4", "MPEG-4", "video/mp4", ".mp4"),
        Video("mkv", "Matroska", "video/x-matroska", ".mkv"),
        Video("webm", "WebM", "video/webm", ".webm"),
        Video("mov", "QuickTime", "video/quicktime", ".mov"),
        Video("avi", "AVI", "video/x-msvideo", ".avi"),
        Video("m4v", "MPEG-4 (m4v)", "video/x-m4v", ".m4v"),
        Video("wmv", "Windows Media", "video/x-ms-wmv", ".wmv"),
        Video("flv", "Flash Video", "video/x-flv", ".flv"),
        Video("mpg", "MPEG-PS", "video/mpeg", ".mpg", ".mpeg"),
        Video("ts", "MPEG-TS", "video/mp2t", ".ts", ".m2ts", ".mts"),
        Video("ogv", "Ogg Video", "video/ogg", ".ogv"),
        Video("3gp", "3GPP", "video/3gpp", ".3gp"),

        // --- Audio -------------------------------------------------------------------
        Audio("mp3", "MP3", "audio/mpeg", ".mp3"),
        Audio("aac", "AAC", "audio/aac", ".aac"),
        Audio("m4a", "MPEG-4 Audio", "audio/mp4", ".m4a"),
        Audio("flac", "FLAC", "audio/flac", ".flac"),
        Audio("wav", "WAV", "audio/wav", ".wav"),
        Audio("opus", "Opus", "audio/opus", ".opus"),
        Audio("ogg", "Ogg Vorbis", "audio/ogg", ".ogg", ".oga"),
        Audio("wma", "Windows Media Audio", "audio/x-ms-wma", ".wma"),
        Audio("aiff", "AIFF", "audio/aiff", ".aiff", ".aif"),

        // --- Images ------------------------------------------------------------------
        Image("jpeg", "JPEG", "image/jpeg", ".jpg", ".jpeg", ".jpe"),
        Image("png", "PNG", "image/png", ".png"),
        Image("webp", "WebP", "image/webp", ".webp"),
        Image("gif", "GIF", "image/gif", ".gif"),
        Image("avif", "AVIF", "image/avif", ".avif"),
        Image("heic", "HEIC", "image/heic", ".heic", ".heif"),
        Image("tiff", "TIFF", "image/tiff", ".tif", ".tiff"),
        Image("bmp", "Bitmap", "image/bmp", ".bmp"),
        Image("ico", "Icone Windows", "image/x-icon", ".ico"),

        // --- RAW photo ---------------------------------------------------------------
        // Jamais proposes en sortie : on developpe un RAW, on n'en fabrique pas.
        Raw("cr2", "Canon RAW 2", ".cr2"),
        Raw("cr3", "Canon RAW 3", ".cr3"),
        Raw("nef", "Nikon RAW", ".nef"),
        Raw("arw", "Sony RAW", ".arw"),
        Raw("dng", "Adobe DNG", ".dng"),
        Raw("orf", "Olympus RAW", ".orf"),
        Raw("rw2", "Panasonic RAW", ".rw2"),
        Raw("raf", "Fujifilm RAW", ".raf"),
        Raw("pef", "Pentax RAW", ".pef"),
        Raw("srw", "Samsung RAW", ".srw"),

        // --- Documents ---------------------------------------------------------------
        Document("pdf", "PDF", "application/pdf", ".pdf"),
        Document("docx", "Word", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
        Document("doc", "Word 97-2003", "application/msword", ".doc"),
        Document("odt", "OpenDocument Texte", "application/vnd.oasis.opendocument.text", ".odt"),
        Document("rtf", "Texte enrichi", "application/rtf", ".rtf"),
        Document("txt", "Texte brut", "text/plain", ".txt"),
        Document("html", "HTML", "text/html", ".html", ".htm"),
        Document("xlsx", "Excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
        Document("xls", "Excel 97-2003", "application/vnd.ms-excel", ".xls"),
        Document("ods", "OpenDocument Classeur", "application/vnd.oasis.opendocument.spreadsheet", ".ods"),
        Document("csv", "CSV", "text/csv", ".csv"),
        Document("pptx", "PowerPoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx"),
        Document("ppt", "PowerPoint 97-2003", "application/vnd.ms-powerpoint", ".ppt"),
        Document("odp", "OpenDocument Presentation", "application/vnd.oasis.opendocument.presentation", ".odp"),
        Document("epub", "EPUB", "application/epub+zip", ".epub"),
    ];

    private static MediaFormat Video(string id, string name, string mime, params string[] extensions) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Family = FormatFamily.Video,
            Extensions = extensions,
            MimeType = mime,
        };

    private static MediaFormat Audio(string id, string name, string mime, params string[] extensions) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Family = FormatFamily.Audio,
            Extensions = extensions,
            MimeType = mime,
        };

    private static MediaFormat Image(string id, string name, string mime, params string[] extensions) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Family = FormatFamily.Image,
            Extensions = extensions,
            MimeType = mime,
        };

    private static MediaFormat Raw(string id, string name, params string[] extensions) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Family = FormatFamily.RawImage,
            Extensions = extensions,
            MimeType = "image/x-dcraw",
            CanBeTarget = false,
        };

    private static MediaFormat Document(string id, string name, string mime, params string[] extensions) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Family = FormatFamily.Document,
            Extensions = extensions,
            MimeType = mime,
        };
}
