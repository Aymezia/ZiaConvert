using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Tools;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>
/// Determine quels encodeurs materiels fonctionnent reellement sur la machine.
/// </summary>
/// <remarks>
/// La liste de <c>ffmpeg -encoders</c> ne suffit pas : elle enumere ce qui a ete compile
/// dans le binaire, pas ce que le materiel present sait faire. Un build complet annonce
/// NVENC, QuickSync et AMF sur une machine qui n'a qu'une seule de ces trois puces.
/// La seule reponse fiable est d'encoder une image et de voir si ca passe.
/// </remarks>
public sealed class HardwareDetector
{
    private static readonly EncoderCandidate[] Candidates =
    [
        new("h264_nvenc"), new("hevc_nvenc"), new("av1_nvenc"),
        new("h264_qsv"), new("hevc_qsv"), new("av1_qsv"),
        new("h264_amf"), new("hevc_amf"), new("av1_amf"),
        new("h264_videotoolbox"), new("hevc_videotoolbox"),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IProcessRunner _runner;
    private readonly IEngineLocator _locator;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _cachePath;

    private HardwareSupport? _cached;

    public HardwareDetector(IProcessRunner runner, IEngineLocator locator, ILogger<HardwareDetector>? logger = null)
    {
        _runner = runner;
        _locator = locator;
        _logger = logger ?? NullLogger<HardwareDetector>.Instance;
        _cachePath = Path.Combine(
            Path.GetDirectoryName(ToolLocator.UserEnginesDirectory) ?? Path.GetTempPath(),
            "hardware-support.json");
    }

    /// <summary>
    /// Rend les capacites materielles, en les detectant au premier appel.
    /// </summary>
    /// <param name="force">Ignore le cache et refait les tests d'encodage.</param>
    public async Task<HardwareSupport> DetectAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!force && _cached is not null)
            {
                return _cached;
            }

            var signature = BuildSignature();

            if (!force && ReadCache() is { } cached && cached.Signature == signature)
            {
                _logger.LogDebug("Capacites materielles relues du cache : {Encoders}", string.Join(", ", cached.WorkingEncoders));
                return _cached = cached;
            }

            var detected = await ProbeAsync(signature, cancellationToken).ConfigureAwait(false);
            WriteCache(detected);

            return _cached = detected;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HardwareSupport> ProbeAsync(string? signature, CancellationToken cancellationToken)
    {
        var ffmpeg = _locator.Locate("ffmpeg");

        if (ffmpeg is null)
        {
            _logger.LogWarning("ffmpeg introuvable : aucune acceleration materielle ne sera proposee.");
            return new HardwareSupport { Signature = signature };
        }

        // Pre-filtrage : inutile de tenter un encodage avec un encodeur absent du binaire.
        var compiled = await ListCompiledEncodersAsync(ffmpeg, cancellationToken).ConfigureAwait(false);
        var working = new List<string>();

        foreach (var candidate in Candidates.Where(c => compiled.Contains(c.Name)))
        {
            if (await CanEncodeAsync(ffmpeg, candidate.Name, cancellationToken).ConfigureAwait(false))
            {
                working.Add(candidate.Name);
            }
        }

        _logger.LogInformation(
            "Encodeurs materiels operationnels : {Encoders}",
            working.Count > 0 ? string.Join(", ", working) : "aucun, repli logiciel");

        return new HardwareSupport { WorkingEncoders = working, Signature = signature };
    }

    private async Task<HashSet<string>> ListCompiledEncodersAsync(string ffmpeg, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest { FileName = ffmpeg, Arguments = ["-hide_banner", "-encoders"] },
            cancellationToken).ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in result.StandardOutput)
        {
            // Format « V....D h264_nvenc   NVIDIA NVENC H.264 encoder ».
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length >= 2 && parts[0].Length == 6)
            {
                names.Add(parts[1]);
            }
        }

        return names;
    }

    /// <summary>
    /// Encode une poignee d'images de synthese vers nulle part. C'est le seul moyen de
    /// declencher l'initialisation du pilote et donc de savoir si l'encodeur repond.
    /// </summary>
    private async Task<bool> CanEncodeAsync(string ffmpeg, string encoder, CancellationToken cancellationToken)
    {
        var arguments = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")
            .Add("-f", "lavfi")
            .Add("-i", "nullsrc=s=256x256:d=0.1")
            .Add("-c:v", encoder)
            .Add("-f", "null")
            .Add("-")
            .Build();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            var result = await _runner
                .RunAsync(new ProcessRequest { FileName = ffmpeg, Arguments = arguments }, timeout.Token)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                _logger.LogDebug("{Encoder} indisponible : {Error}", encoder, result.StandardErrorText);
            }

            return result.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Un pilote en mauvais etat peut bloquer indefiniment : on le considere absent.
            _logger.LogWarning("{Encoder} n'a pas repondu dans le delai imparti.", encoder);
            return false;
        }
    }

    /// <summary>
    /// Identifie le binaire ffmpeg par son chemin, sa taille et sa date. Une mise a jour
    /// de ffmpeg change la signature et force donc une nouvelle detection.
    /// </summary>
    private string? BuildSignature()
    {
        var ffmpeg = _locator.Locate("ffmpeg");

        if (ffmpeg is null || !File.Exists(ffmpeg))
        {
            return null;
        }

        var info = new FileInfo(ffmpeg);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
    }

    private HardwareSupport? ReadCache()
    {
        try
        {
            return File.Exists(_cachePath)
                ? JsonSerializer.Deserialize<HardwareSupport>(File.ReadAllText(_cachePath), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un cache illisible n'est pas une erreur : on redetecte.
            _logger.LogDebug(ex, "Cache de detection materielle illisible, nouvelle detection.");
            return null;
        }
    }

    private void WriteCache(HardwareSupport support)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(support, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Impossible d'ecrire le cache de detection materielle.");
        }
    }

    private sealed record EncoderCandidate(string Name);
}
