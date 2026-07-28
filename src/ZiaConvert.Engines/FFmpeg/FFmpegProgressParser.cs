using System.Globalization;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>Un releve d'avancement complet, tel qu'emis par <c>-progress</c>.</summary>
internal sealed record FFmpegProgressSnapshot
{
    public long? Frame { get; init; }

    public double? Fps { get; init; }

    /// <summary>Position atteinte dans le media de sortie.</summary>
    public TimeSpan? OutTime { get; init; }

    public long? TotalSize { get; init; }

    /// <summary>Vitesse relative au temps reel, sans le suffixe <c>x</c>.</summary>
    public double? Speed { get; init; }

    /// <summary>Vrai sur le dernier releve, quand ffmpeg annonce <c>progress=end</c>.</summary>
    public bool IsFinal { get; init; }
}

/// <summary>
/// Lit le flux de <c>ffmpeg -progress pipe:1</c>.
/// </summary>
/// <remarks>
/// ffmpeg emet des blocs de lignes <c>cle=valeur</c> termines par une ligne <c>progress=</c>.
/// C'est la seule source d'avancement fiable : la barre affichee sur la sortie d'erreur
/// est destinee a un humain et sa mise en forme change d'une version a l'autre.
/// </remarks>
internal sealed class FFmpegProgressParser
{
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);

    /// <summary>
    /// Absorbe une ligne de sortie standard.
    /// </summary>
    /// <returns>Un releve complet a la fin d'un bloc, sinon <c>null</c>.</returns>
    public FFmpegProgressSnapshot? Feed(string line)
    {
        var separator = line.IndexOf('=', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return null;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        if (!string.Equals(key, "progress", StringComparison.Ordinal))
        {
            _fields[key] = value;
            return null;
        }

        var snapshot = Build(isFinal: string.Equals(value, "end", StringComparison.Ordinal));
        _fields.Clear();

        return snapshot;
    }

    private FFmpegProgressSnapshot Build(bool isFinal) => new()
    {
        Frame = ReadLong("frame"),
        Fps = ReadDouble("fps"),
        OutTime = ReadOutTime(),
        TotalSize = ReadLong("total_size"),
        Speed = ReadSpeed(),
        IsFinal = isFinal,
    };

    /// <summary>
    /// Lit la position courante.
    /// </summary>
    /// <remarks>
    /// <c>out_time_us</c> est en microsecondes. <c>out_time_ms</c> l'est aussi malgre son nom :
    /// c'est une bizarrerie historique de ffmpeg, et l'utiliser comme des millisecondes
    /// donnerait un avancement mille fois trop rapide. On ne s'en sert donc pas.
    /// </remarks>
    private TimeSpan? ReadOutTime()
    {
        if (ReadLong("out_time_us") is { } microseconds and >= 0)
        {
            return TimeSpan.FromMicroseconds(microseconds);
        }

        // Repli sur la forme lisible « 00:01:23.456789 ».
        if (_fields.TryGetValue("out_time", out var text) &&
            TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private double? ReadSpeed()
    {
        if (!_fields.TryGetValue("speed", out var raw))
        {
            return null;
        }

        // Forme « 2.53x », ou « N/A » tant que ffmpeg n'a pas assez d'echantillons.
        var trimmed = raw.TrimEnd('x', 'X').Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
            ? speed
            : null;
    }

    private long? ReadLong(string key) =>
        _fields.TryGetValue(key, out var raw) &&
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private double? ReadDouble(string key) =>
        _fields.TryGetValue(key, out var raw) &&
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
