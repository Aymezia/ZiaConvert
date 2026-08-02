using ZiaConvert.Core.Jobs;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Routing;

namespace ZiaConvert.Core.Tests.Jobs;

/// <summary>
/// Verification post-conversion : une sortie beaucoup plus courte que la source, malgre
/// un moteur qui n'a rien signale d'anormal, doit se voir signalee comme suspecte.
/// </summary>
public sealed class ConversionExecutorVerificationTests : IDisposable
{
    private readonly FakeEngine _engine = new();
    private readonly FakeMediaProbe _probe = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("ziaconvert-verify-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Sans consequence pour la serie de tests.
        }
    }

    [Fact]
    public async Task N_avertit_pas_quand_la_duree_correspond()
    {
        _probe.Duration = TimeSpan.FromMinutes(2);

        var result = await ExecuteAsync(sourceDuration: TimeSpan.FromMinutes(2));

        Assert.True(result.Success);
        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task Avertit_quand_la_sortie_est_beaucoup_plus_courte_que_la_source()
    {
        // Une sortie a 10% de la duree attendue est le profil typique d'un fichier
        // tronque par un arret brutal en cours d'ecriture.
        _probe.Duration = TimeSpan.FromSeconds(12);

        var result = await ExecuteAsync(sourceDuration: TimeSpan.FromMinutes(2));

        Assert.True(result.Success, "Un fichier suspect reste un succes : il existe, le moteur n'a rien signale.");
        Assert.NotNull(result.VerificationWarning);
        Assert.Contains("tronque", result.VerificationWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tolere_un_petit_ecart_d_arrondi_de_conteneur()
    {
        // 1,2 s d'ecart sur un extrait de 2 minutes est un arrondi normal, pas un signal
        // de troncature : en dessous du seuil de tolerance, aucun avertissement.
        _probe.Duration = TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1.2);

        var result = await ExecuteAsync(sourceDuration: TimeSpan.FromMinutes(2));

        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task N_avertit_pas_quand_un_extrait_a_ete_demande()
    {
        // La duree de sortie est CENSEE differer de la source des qu'un debut/fin est
        // precise : comparer les deux produirait un faux positif systematique.
        _probe.Duration = TimeSpan.FromSeconds(10);

        var options = new VideoOptions { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(10) };
        var result = await ExecuteAsync(sourceDuration: TimeSpan.FromMinutes(2), options);

        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task N_avertit_pas_sans_sonde_fournie()
    {
        var router = new ConversionRouter([_engine]);
        var executor = new ConversionExecutor(router); // pas de sonde : verification desactivee

        var request = BuildRequest(TimeSpan.FromMinutes(2), ConversionOptions.None);
        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task N_avertit_pas_pour_une_cible_image()
    {
        // La comparaison de duree n'a de sens que pour la video et l'audio.
        _probe.Duration = TimeSpan.FromSeconds(1);

        var registry = FormatRegistry.Default;
        var request = new ConversionRequest
        {
            InputPath = Path.Combine(_directory, "photo.png"),
            OutputPath = Path.Combine(_directory, "photo.jpg"),
            SourceFormat = registry.GetByPath("photo.png"),
            TargetFormat = registry.GetByPath("photo.jpg"),
            SourceInfo = new MediaInfo { Duration = TimeSpan.FromMinutes(2) },
        };

        var router = new ConversionRouter([_engine]);
        var executor = new ConversionExecutor(router, _probe);

        var result = await executor.ExecuteAsync(request);

        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task Un_echec_de_la_sonde_de_verification_devient_un_avertissement_pas_un_echec()
    {
        // La conversion a reellement reussi (le moteur l'a dit) : une sonde de
        // verification qui trebuche ne doit jamais transformer ca en echec.
        _probe.ThrowOnProbe = true;

        var result = await ExecuteAsync(sourceDuration: TimeSpan.FromMinutes(2));

        Assert.True(result.Success);
        Assert.NotNull(result.VerificationWarning);
    }

    private async Task<ConversionResult> ExecuteAsync(
        TimeSpan sourceDuration,
        ConversionOptions? options = null)
    {
        var router = new ConversionRouter([_engine]);
        var executor = new ConversionExecutor(router, _probe);

        var request = BuildRequest(sourceDuration, options ?? ConversionOptions.None);

        return await executor.ExecuteAsync(request);
    }

    private ConversionRequest BuildRequest(TimeSpan sourceDuration, ConversionOptions options)
    {
        var registry = FormatRegistry.Default;

        return new ConversionRequest
        {
            InputPath = Path.Combine(_directory, "source.mp4"),
            OutputPath = Path.Combine(_directory, "sortie.mkv"),
            SourceFormat = registry.GetByPath("source.mp4"),
            TargetFormat = registry.GetByPath("sortie.mkv"),
            Options = options,
            SourceInfo = new MediaInfo { Duration = sourceDuration },
        };
    }
}
