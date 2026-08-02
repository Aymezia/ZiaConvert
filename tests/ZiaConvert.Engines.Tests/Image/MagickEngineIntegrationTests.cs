using System.Globalization;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;

namespace ZiaConvert.Engines.Tests.Image;

[Collection("magick")]
[Trait("Category", "Integration")]
public sealed class MagickEngineIntegrationTests
{
    private readonly MagickMediaFixture _fixture;

    public MagickEngineIntegrationTests(MagickMediaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Convertit_png_vers_jpeg()
    {
        var output = _fixture.OutputPath("photo.jpg");

        var result = await _fixture.Services.Executor.ExecuteAsync(Request(_fixture.SamplePng, output));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task Convertit_png_vers_webp()
    {
        var output = _fixture.OutputPath("photo.webp");

        var result = await _fixture.Services.Executor.ExecuteAsync(Request(_fixture.SamplePng, output));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task Ne_laisse_aucun_fichier_temporaire_apres_une_reussite()
    {
        var output = _fixture.OutputPath("propre.jpg");
        var request = Request(_fixture.SamplePng, output);

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.Exists(request.WorkingPath));
    }

    [Fact]
    public async Task Redimensionne_l_image()
    {
        var output = _fixture.OutputPath("redimensionne.png");
        var request = Request(_fixture.SamplePng, output, new ImageOptions { Width = 200 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);
        Assert.True(result.Success, result.ErrorMessage);

        var info = await IdentifyAsync(output);
        Assert.Equal(200, info.Width);

        // Le ratio d'origine est 400x300 (4:3) : une largeur de 200 doit donner une
        // hauteur de 150 si le ratio a bien ete conserve.
        Assert.Equal(150, info.Height);
    }

    [Fact]
    public async Task Efface_le_canal_alpha_en_convertissant_vers_un_format_sans_transparence()
    {
        var output = _fixture.OutputPath("opaque.jpg");
        var request = Request(_fixture.SamplePngWithAlpha, output);

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var info = await IdentifyAsync(output);
        Assert.DoesNotContain("a", info.Channels, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explique_un_fichier_source_illisible()
    {
        var request = Request(_fixture.CorruptFile, _fixture.OutputPath("jamais.png"));

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("corrompu.jpg", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuse_d_ecraser_une_sortie_existante_sans_autorisation()
    {
        var output = _fixture.OutputPath("existe-deja.jpg");
        await File.WriteAllTextAsync(output, "contenu precieux");

        var result = await _fixture.Services.Executor.ExecuteAsync(Request(_fixture.SamplePng, output));

        Assert.False(result.Success);
        Assert.Equal("contenu precieux", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Ecrase_la_sortie_quand_c_est_demande()
    {
        var output = _fixture.OutputPath("ecrasable.jpg");
        await File.WriteAllTextAsync(output, "a remplacer");

        var request = Request(_fixture.SamplePng, output) with { Overwrite = true };
        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 100);
    }

    [Fact]
    public void Route_un_RAW_vers_une_image_meme_sans_fichier_valide()
    {
        // CanHandle ne regarde que les familles de format, jamais le contenu du fichier :
        // ce test n'a donc pas besoin d'un vrai negatif pour verifier le routage.
        var request = _fixture.Services.Router.CreateRequest("photo.cr2", "photo.jpg");

        var engine = _fixture.Services.Router.SelectEngine(request);

        Assert.Equal("imagemagick", engine.Name);
    }

    [Fact]
    public async Task Developpe_un_vrai_fichier_RAW()
    {
        // Contrairement a un fichier video ou audio, un negatif RAW ne se synthetise pas :
        // c'est un enregistrement de la mosaique d'un capteur reel. Sans exemplaire
        // trouve sur la machine (voir MagickMediaFixture.FindRealRawFile), ce test n'a
        // rien a verifier ; le routage seul est couvert par le test precedent.
        if (_fixture.RawSample is null)
        {
            return;
        }

        var output = _fixture.OutputPath("developpe.jpg");
        var request = Request(_fixture.RawSample, output);

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 0);
    }

    private ConversionRequest Request(string input, string output, ConversionOptions? options = null) =>
        _fixture.Services.Router.CreateRequest(input, output, options, overwrite: false);

    private async Task<(int Width, int Height, string Channels)> IdentifyAsync(string path)
    {
        var magick = _fixture.Services.Locator.Locate("magick")!;
        var runner = _fixture.Services.ProcessRunner;

        var result = await runner.RunAsync(new Core.Processes.ProcessRequest
        {
            FileName = magick,
            Arguments = ["identify", "-format", "%w %h %[channels]", path],
        });

        Assert.True(result.Success, result.StandardErrorText);

        var parts = result.StandardOutputText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return (
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            parts.Length > 2 ? parts[2] : string.Empty);
    }
}
