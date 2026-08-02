using System.Diagnostics;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Engines.Upscale;

namespace ZiaConvert.Engines.Tests.Upscale;

[Collection("realesrgan")]
[Trait("Category", "Integration")]
public sealed class RealEsrganEngineIntegrationTests
{
    private readonly RealEsrganMediaFixture _fixture;

    public RealEsrganEngineIntegrationTests(RealEsrganMediaFixture fixture) => _fixture = fixture;

    [Fact]
    public void N_est_selectionne_que_si_la_demande_porte_des_UpscaleOptions()
    {
        // Une conversion ordinaire jpg -> png doit passer par ImageMagick, pas par
        // Real-ESRGAN, meme si les deux moteurs savent techniquement produire un jpg
        // depuis un png. C'est le type d'options, pas le couple de formats, qui tranche.
        var ordinary = _fixture.Services.Router.CreateRequest(_fixture.SampleImage, _fixture.OutputPath("ordinaire.png"));

        var engine = _fixture.Services.Router.SelectEngine(ordinary);

        Assert.Equal("imagemagick", engine.Name);
    }

    [Fact]
    public void Est_selectionne_quand_la_demande_porte_des_UpscaleOptions()
    {
        var request = _fixture.Services.Router.CreateRequest(
            _fixture.SampleImage, _fixture.OutputPath("agrandi.png"), new UpscaleOptions());

        var engine = _fixture.Services.Router.SelectEngine(request);

        Assert.Equal("realesrgan", engine.Name);
    }

    [Fact]
    public void Rejette_un_RAW_en_source()
    {
        // L'outil ne lit que jpg/png/webp, confirme par son propre message d'aide : un
        // RAW doit d'abord etre developpe par ImageMagick, l'agrandissement ne peut pas
        // s'en charger directement.
        var request = _fixture.Services.Router.CreateRequest(
            "photo.cr2", "photo.png", new UpscaleOptions());

        Assert.Throws<UnsupportedConversionException>(() => _fixture.Services.Router.SelectEngine(request));
    }

    [Fact]
    public async Task Agrandit_l_image_au_facteur_demande()
    {
        var output = _fixture.OutputPath("x3.png");
        var request = Request(_fixture.SampleImage, output, new UpscaleOptions { Factor = 3 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var (width, height) = await IdentifyAsync(output);

        // Source generee a 120x90 (voir RealEsrganMediaFixture) : le facteur 3 doit
        // donner exactement 360x270.
        Assert.Equal(360, width);
        Assert.Equal(270, height);
    }

    [Fact]
    public async Task Ne_laisse_aucun_fichier_temporaire_apres_une_reussite()
    {
        // Le fichier de travail de ce moteur n'utilise pas le suffixe .part partage par
        // les autres (l'outil rejette cette extension), donc ce test verifie
        // specifiquement l'absence de tout fichier cache residuel dans le dossier.
        var output = _fixture.OutputPath("propre.png");
        var before = Directory.GetFiles(_fixture.WorkDirectory).Length;

        var result = await _fixture.Services.Executor.ExecuteAsync(
            Request(_fixture.SampleImage, output, new UpscaleOptions()));

        Assert.True(result.Success, result.ErrorMessage);

        var after = Directory.GetFiles(_fixture.WorkDirectory);
        Assert.Equal(before + 1, after.Length);
        Assert.Contains(output, after);
    }

    [Fact]
    public async Task Refuse_d_ecraser_une_sortie_existante_sans_autorisation()
    {
        var output = _fixture.OutputPath("existe-deja.png");
        await File.WriteAllTextAsync(output, "contenu precieux");

        var result = await _fixture.Services.Executor.ExecuteAsync(
            Request(_fixture.SampleImage, output, new UpscaleOptions()));

        Assert.False(result.Success);
        Assert.Equal("contenu precieux", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task L_annulation_ne_laisse_aucun_fichier()
    {
        // Image volontairement grande (voir RealEsrganMediaFixture) : sans ca,
        // l'agrandissement se terminerait avant meme d'avoir eu le temps de l'annuler.
        var output = _fixture.OutputPath("annule.png");
        var request = Request(_fixture.LargeImage, output, new UpscaleOptions { Factor = 4 });

        using var cancellation = new CancellationTokenSource();
        var conversion = _fixture.Services.Executor.ExecuteAsync(request, cancellationToken: cancellation.Token);

        await Task.Delay(TimeSpan.FromSeconds(1));
        await cancellation.CancelAsync();

        // L'annulation est propagee, pas convertie en ConversionResult en echec — meme
        // contrat que les autres moteurs (voir FFmpegEngineIntegrationTests).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversion);

        Assert.False(File.Exists(output), "Une sortie a moitie ecrite a ete laissee en place.");

        // WorkDirectory est partage par toute la classe de tests (ICollectionFixture) :
        // on ne peut pas verifier qu'il est vide, seulement que le fichier de travail
        // cache propre a CE moteur (prefixe par un point) n'y a rien laisse.
        Assert.DoesNotContain(
            Directory.GetFiles(_fixture.WorkDirectory),
            f => Path.GetFileName(f).StartsWith('.'));
    }

    [Fact]
    public async Task L_estimation_est_du_bon_ordre_de_grandeur()
    {
        // La calibration se declenche ici si elle n'a pas deja tourne dans un test
        // precedent : premiere execution plus lente, sans consequence sur l'assertion.
        var engine = _fixture.Services.Engines.OfType<RealEsrganEngine>().Single();

        var stopwatch = Stopwatch.StartNew();
        var estimate = await engine.EstimateDurationAsync(120, 90, new UpscaleOptions { Factor = 3 });
        stopwatch.Stop();

        Assert.NotNull(estimate);
        Assert.True(estimate.Value > TimeSpan.Zero);

        // La calibration elle-meme ne doit pas exploser : deux petits agrandissements,
        // quelques secondes en tout, jamais des dizaines de secondes.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"La calibration a pris {stopwatch.Elapsed.TotalSeconds:0.0} s.");
    }

    private ConversionRequest Request(string input, string output, ConversionOptions options) =>
        _fixture.Services.Router.CreateRequest(input, output, options, overwrite: false);

    private async Task<(int Width, int Height)> IdentifyAsync(string path)
    {
        var magick = _fixture.Services.Locator.Locate("magick")!;

        var result = await _fixture.Services.ProcessRunner.RunAsync(new Core.Processes.ProcessRequest
        {
            FileName = magick,
            Arguments = ["identify", "-format", "%w %h", path],
        });

        Assert.True(result.Success, result.StandardErrorText);

        var parts = result.StandardOutputText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
