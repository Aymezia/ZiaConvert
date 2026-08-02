using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;

namespace ZiaConvert.Core.Tests.Jobs;

/// <summary>
/// Sonde de substitution : rend une duree fixee d'avance plutot que d'analyser un vrai
/// fichier, pour eprouver la verification post-conversion sans dependre de ffmpeg.
/// </summary>
internal sealed class FakeMediaProbe : IMediaProbe
{
    /// <summary>Duree rendue pour chaque appel. <c>null</c> simule une sonde qui ne sait pas mesurer.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Fait echouer la sonde, pour simuler une sortie illisible.</summary>
    public bool ThrowOnProbe { get; set; }

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ThrowOnProbe)
        {
            throw new ConversionException($"Sonde simulee en echec pour {path}.");
        }

        return Task.FromResult(new MediaInfo { Duration = Duration });
    }
}
