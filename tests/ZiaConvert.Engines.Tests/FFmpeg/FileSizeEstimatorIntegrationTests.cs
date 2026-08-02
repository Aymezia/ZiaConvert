using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Routing;
using ZiaConvert.Engines.FFmpeg;

namespace ZiaConvert.Engines.Tests.FFmpeg;

[Collection("ffmpeg")]
[Trait("Category", "Integration")]
public sealed class FileSizeEstimatorIntegrationTests
{
    private readonly FFmpegMediaFixture _fixture;
    private readonly FormatRegistry _registry = FormatRegistry.Default;

    public FileSizeEstimatorIntegrationTests(FFmpegMediaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mesure_directement_la_taille_d_un_remux()
    {
        // Une copie de flux ne change pratiquement pas la taille : mesure directe de la
        // source, aucun echantillon a encoder.
        var request = await PrepareAsync(_fixture.ShortVideo, "estimation-remux.mkv", new VideoOptions());

        var estimate = await _fixture.Services.FileSizeEstimator.EstimateAsync(request);

        Assert.NotNull(estimate);
        Assert.False(estimate.IsSampled);
        Assert.Equal(new FileInfo(_fixture.ShortVideo).Length, estimate.EstimatedBytes);
    }

    [Fact]
    public async Task Extrapole_a_partir_d_un_echantillon_reel_pour_un_reencodage()
    {
        var options = new VideoOptions { Codec = VideoCodec.H265, Quality = 28 };
        var request = await PrepareAsync(_fixture.ShortVideo, "estimation-crf.mp4", options);

        var estimate = await _fixture.Services.FileSizeEstimator.EstimateAsync(request);

        Assert.NotNull(estimate);
        Assert.True(estimate.IsSampled);
        Assert.True(estimate.EstimatedBytes > 0);

        // La source generee (voir FFmpegMediaFixture) tient en quelques centaines de
        // kilo-octets : un HEVC CRF28 ne peut raisonnablement pas depasser la source
        // brute de plusieurs ordres de grandeur.
        Assert.True(estimate.EstimatedBytes < new FileInfo(_fixture.ShortVideo).Length * 10);
    }

    [Fact]
    public async Task Ne_laisse_aucun_fichier_temporaire_apres_l_estimation()
    {
        var options = new VideoOptions { Codec = VideoCodec.H265, Quality = 28 };
        var request = await PrepareAsync(_fixture.ShortVideo, "estimation-propre.mp4", options);

        await _fixture.Services.FileSizeEstimator.EstimateAsync(request);

        // L'echantillon s'ecrit dans son propre dossier temporaire, distinct du dossier
        // de sortie de la conversion : rien ne doit y apparaitre.
        Assert.False(File.Exists(_fixture.OutputPath("estimation-propre.mp4")));
    }

    [Fact]
    public async Task Rend_null_pour_une_cible_audio()
    {
        var request = await PrepareAsync(_fixture.ShortVideo, "estimation.mp3", new AudioOptions());

        var estimate = await _fixture.Services.FileSizeEstimator.EstimateAsync(request);

        Assert.Null(estimate);
    }

    [Fact]
    public async Task Rend_null_sans_duree_source_connue()
    {
        var request = new ConversionRequest
        {
            InputPath = _fixture.ShortVideo,
            OutputPath = _fixture.OutputPath("sans-duree.mkv"),
            SourceFormat = _registry.GetByPath(_fixture.ShortVideo),
            TargetFormat = _registry.GetByPath("sans-duree.mkv"),
            Options = new VideoOptions(),

            // Pas de SourceInfo : rien a comparer, l'estimation n'a pas de reference.
        };

        var estimate = await _fixture.Services.FileSizeEstimator.EstimateAsync(request);

        Assert.Null(estimate);
    }

    private async Task<ConversionRequest> PrepareAsync(string input, string outputName, ConversionOptions options)
    {
        var request = _fixture.Services.Router.CreateRequest(input, _fixture.OutputPath(outputName), options);

        return await _fixture.Services.Router.PrepareAsync(request);
    }
}
