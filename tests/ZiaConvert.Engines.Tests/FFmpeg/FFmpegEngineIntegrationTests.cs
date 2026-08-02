using System.Diagnostics;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;

namespace ZiaConvert.Engines.Tests.FFmpeg;

[Collection("ffmpeg")]
[Trait("Category", "Integration")]
public sealed class FFmpegEngineIntegrationTests
{
    private readonly FFmpegMediaFixture _fixture;

    public FFmpegEngineIntegrationTests(FFmpegMediaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Convertit_mp4_vers_mkv_par_copie_de_flux()
    {
        var output = _fixture.OutputPath("remux.mkv");
        var request = Request(_fixture.ShortVideo, output);

        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.Services.Executor.ExecuteAsync(request);
        stopwatch.Stop();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.Contains("copie", result.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Une copie de flux ne reencode rien : elle doit rester quasi instantanee.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"La copie de flux a pris {stopwatch.Elapsed.TotalSeconds:0.0} s, un reencodage a du se declencher.");
    }

    [Fact]
    public async Task Remuxe_un_rip_DVD_malgre_ses_horodatages_discontinus()
    {
        // Le cas reel qui a motive --remux-only : un flux MPEG-2/AC3 sans genpts fait
        // echouer le muxer matroska avec « Can't write packet with unknown timestamp ».
        var output = _fixture.OutputPath("depuis-dvd.mkv");
        var request = Request(_fixture.DvdRip, output, new VideoOptions { RemuxOnly = true });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("copie", result.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var info = await _fixture.Services.Probe.ProbeAsync(output);

        // Toujours mpeg2video/ac3 : un vrai remux ne touche jamais aux codecs.
        Assert.Equal("mpeg2video", info.PrimaryVideo?.CodecName);
        Assert.Equal("ac3", info.PrimaryAudio?.CodecName);
    }

    [Fact]
    public async Task RemuxOnly_echoue_plutot_que_de_reencoder_silencieusement()
    {
        // mp4 n'accepte pas mpeg2video : sans RemuxOnly, ce serait un reencodage complet
        // aux reglages par defaut, une surprise pour qui voulait seulement re-emballer.
        var output = _fixture.OutputPath("depuis-dvd.mp4");
        var request = Request(_fixture.DvdRip, output, new VideoOptions { RemuxOnly = true });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Contains("mpeg2video", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Conserve_seulement_la_piste_audio_choisie()
    {
        // Source a deux pistes (voir FFmpegMediaFixture) : la piste 2 (fra, 880 Hz) doit
        // survivre seule, la piste 1 (eng, 440 Hz) doit disparaitre.
        var output = _fixture.OutputPath("piste-choisie.mkv");
        var request = Request(_fixture.MultiTrackVideo, output, new VideoOptions { AudioTrackIndex = 2 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);
        Assert.True(result.Success, result.ErrorMessage);

        var info = await _fixture.Services.Probe.ProbeAsync(output);
        var audioStreams = info.Streams.Where(s => s.Kind == MediaStreamKind.Audio).ToList();

        Assert.Single(audioStreams);
    }

    [Fact]
    public async Task Integre_un_sous_titre_externe_sans_reencoder()
    {
        var output = _fixture.OutputPath("avec-sous-titre.mkv");
        var request = Request(_fixture.ShortVideo, output, new VideoOptions
        {
            ExternalSubtitles = [new SubtitleImport { FilePath = _fixture.SubtitleFile, Language = "fre" }],
        });

        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.Services.Executor.ExecuteAsync(request);
        stopwatch.Stop();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("copie", result.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Integrer un sous-titre ne doit jamais declencher un reencodage : ca reste une
        // simple copie de flux, tout comme sans sous-titre.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"L'integration a pris {stopwatch.Elapsed.TotalSeconds:0.0} s, un reencodage a du se declencher.");

        var info = await _fixture.Services.Probe.ProbeAsync(output);
        var subtitleStreams = info.Streams.Where(s => s.Kind == MediaStreamKind.Subtitle).ToList();

        Assert.Single(subtitleStreams);
    }

    [Fact]
    public async Task Refuse_les_sous_titres_externes_vers_un_conteneur_autre_que_mkv()
    {
        var output = _fixture.OutputPath("avec-sous-titre.mp4");
        var request = Request(_fixture.ShortVideo, output, new VideoOptions
        {
            ExternalSubtitles = [new SubtitleImport { FilePath = _fixture.SubtitleFile }],
        });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Contains("mkv", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task N_avertit_pas_sur_une_conversion_normale()
    {
        // Verifie avec la vraie sonde FFprobe (pas un double) : une conversion saine ne
        // doit jamais declencher de faux positif sur la comparaison de duree.
        var output = _fixture.OutputPath("normale.mkv");
        var request = Request(_fixture.ShortVideo, output, new VideoOptions { Codec = VideoCodec.H265 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.VerificationWarning);
    }

    [Fact]
    public async Task Ne_laisse_aucun_fichier_temporaire_apres_une_reussite()
    {
        var output = _fixture.OutputPath("propre.mkv");
        var request = Request(_fixture.ShortVideo, output);

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.Exists(request.WorkingPath), "Le fichier de travail n'a pas ete renomme.");
    }

    [Fact]
    public async Task Extrait_la_bande_son()
    {
        var output = _fixture.OutputPath("bande-son.mp3");

        var result = await _fixture.Services.Executor.ExecuteAsync(Request(_fixture.ShortVideo, output));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task Produit_un_gif()
    {
        var output = _fixture.OutputPath("anime.gif");
        var request = Request(
            _fixture.ShortVideo,
            output,
            new GifOptions { FrameRate = 10, Width = 240 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task Redimensionne_la_video()
    {
        var output = _fixture.OutputPath("redimensionne.mp4");
        var request = Request(_fixture.ShortVideo, output, new VideoOptions { Width = 320 });

        var result = await _fixture.Services.Executor.ExecuteAsync(request);
        Assert.True(result.Success, result.ErrorMessage);

        var info = await _fixture.Services.Probe.ProbeAsync(output);
        Assert.Equal(320, info.PrimaryVideo?.Width);
    }

    [Fact]
    public async Task Rend_un_avancement_croissant_et_borne()
    {
        var output = _fixture.OutputPath("avancement.mp4");
        var request = Request(_fixture.ShortVideo, output, new VideoOptions { Width = 320 });

        var reported = new List<double>();
        var progress = new Progress<ConversionProgress>(p =>
        {
            if (p.Percent is { } percent)
            {
                reported.Add(percent);
            }
        });

        var result = await _fixture.Services.Executor.ExecuteAsync(request, progress);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(reported, p => Assert.InRange(p, 0d, 100d));

        // Un avancement qui reculerait trahirait une mauvaise lecture de la position.
        Assert.Equal(reported.Order(), reported);
    }

    [Fact]
    public async Task L_annulation_ne_laisse_ni_sortie_ni_fichier_temporaire()
    {
        // Le point le plus important de tout le moteur : une conversion interrompue ne
        // doit jamais laisser derriere elle un fichier qui ressemble a un resultat valide.
        var output = _fixture.OutputPath("annule.mp4");
        var request = Request(
            _fixture.LongVideo,
            output,
            new VideoOptions
            {
                Codec = VideoCodec.H265,
                Hardware = HardwareAcceleration.None,
                Width = 1280,
            });

        using var cancellation = new CancellationTokenSource();
        var conversion = _fixture.Services.Executor.ExecuteAsync(request, cancellationToken: cancellation.Token);

        // On laisse l'encodage demarrer pour de bon avant de l'interrompre.
        await Task.Delay(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversion);

        Assert.False(File.Exists(output), "Une sortie a moitie ecrite a ete laissee en place.");
        Assert.False(File.Exists(request.WorkingPath), "Le fichier de travail n'a pas ete efface.");
    }

    [Fact]
    public async Task Explique_un_fichier_source_illisible()
    {
        var request = Request(_fixture.CorruptFile, _fixture.OutputPath("jamais.mkv"));

        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);

        // Le message doit parler du fichier, pas rendre la sortie brute de ffmpeg.
        Assert.Contains("corrompu.mp4", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuse_d_ecraser_une_sortie_existante_sans_autorisation()
    {
        var output = _fixture.OutputPath("existe-deja.mkv");
        await File.WriteAllTextAsync(output, "contenu precieux");

        var result = await _fixture.Services.Executor.ExecuteAsync(Request(_fixture.ShortVideo, output));

        Assert.False(result.Success);
        Assert.Equal("contenu precieux", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Ecrase_la_sortie_quand_c_est_demande()
    {
        var output = _fixture.OutputPath("ecrasable.mkv");
        await File.WriteAllTextAsync(output, "a remplacer");

        var request = Request(_fixture.ShortVideo, output) with { Overwrite = true };
        var result = await _fixture.Services.Executor.ExecuteAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(new FileInfo(output).Length > 100);
    }

    [Fact]
    public void Rejette_une_conversion_sans_moteur()
    {
        // Aucun moteur ne sait produire un document a partir d'une video : le refus doit
        // etre explicite, pas un echec obscur au moment de l'execution.
        var request = _fixture.Services.Router.CreateRequest(
            _fixture.ShortVideo,
            _fixture.OutputPath("impossible.docx"));

        Assert.Throws<UnsupportedConversionException>(() => _fixture.Services.Router.SelectEngine(request));
    }

    private ConversionRequest Request(string input, string output, ConversionOptions? options = null) =>
        _fixture.Services.Router.CreateRequest(input, output, options, overwrite: false);
}
