using System.Globalization;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Routing;
using ZiaConvert.Engines.FFmpeg;

namespace ZiaConvert.Engines.Tests.FFmpeg;

public sealed class FFmpegArgsBuilderTests
{
    private static readonly HardwareSupport NoHardware = new();

    private static readonly HardwareSupport Nvenc = new()
    {
        WorkingEncoders = ["h264_nvenc", "hevc_nvenc", "av1_nvenc"],
    };

    private readonly FFmpegArgsBuilder _builder = new();

    [Fact]
    public void Copie_les_flux_quand_le_conteneur_cible_les_accepte()
    {
        var plan = _builder.Build(Request("video.mp4", "video.mkv"), NoHardware);

        Assert.True(plan.IsRemux);
        Assert.Contains("copy", plan.Arguments);
    }

    [Fact]
    public void Reencode_quand_un_codec_precis_est_demande()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Codec = VideoCodec.H265 });

        var plan = _builder.Build(request, NoHardware);

        Assert.False(plan.IsRemux);
        Assert.Contains("libx265", plan.Arguments);
    }

    [Fact]
    public void Reencode_des_qu_un_redimensionnement_est_demande()
    {
        // Le moindre filtre impose de decoder puis reencoder : la copie devient impossible.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Width = 1280 });

        var plan = _builder.Build(request, NoHardware);

        Assert.False(plan.IsRemux);
        Assert.Contains(plan.Arguments, a => a.StartsWith("scale=1280:", StringComparison.Ordinal));
    }

    [Fact]
    public void Reencode_quand_les_codecs_sont_incompatibles_avec_la_cible()
    {
        // h264 ne rentre pas dans un WebM, quoi qu'on demande.
        var plan = _builder.Build(Request("video.mp4", "video.webm"), NoHardware);

        Assert.False(plan.IsRemux);
        Assert.Contains("libvpx-vp9", plan.Arguments);
    }

    [Fact]
    public void Explique_pourquoi_il_reencode()
    {
        // Le message remonte jusqu'a l'utilisateur : c'est ce qui justifie une conversion
        // longue la ou il en attendait une instantanee.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { AllowRemux = false });

        var plan = _builder.Build(request, NoHardware);

        Assert.False(plan.IsRemux);
        Assert.NotEmpty(plan.Description);
    }

    [Fact]
    public void RemuxOnly_ne_change_rien_quand_le_remux_est_possible()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions { RemuxOnly = true });

        var plan = _builder.Build(request, NoHardware);

        Assert.True(plan.IsRemux);
    }

    [Fact]
    public void RemuxOnly_regenere_les_horodatages_du_remux()
    {
        // Verifie empiriquement sur un flux MPEG-2/AC3 (profil rip DVD) : sans ce
        // reglage, la copie de flux vers matroska echoue avec « Can't write packet with
        // unknown timestamp ». Ca ne touche que la metadonnee de temps, pas les donnees
        // encodees : ca reste un remux au sens strict.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { RemuxOnly = true });

        var plan = _builder.Build(request, NoHardware);

        Assert.Contains("+genpts", plan.Arguments);
    }

    [Fact]
    public void N_ajoute_pas_de_genpts_hors_remux_only()
    {
        // Reserve au remux explicite : le chemin de reencodage, non teste avec ce
        // reglage, ne doit pas en heriter par accident.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Codec = VideoCodec.H265 });

        var plan = _builder.Build(request, NoHardware);

        Assert.DoesNotContain("+genpts", plan.Arguments);
    }

    [Fact]
    public void RemuxOnly_echoue_clairement_quand_le_remux_est_impossible()
    {
        // h264 ne rentre pas dans un WebM : sans RemuxOnly, ce serait un reencodage
        // silencieux aux reglages par defaut. Avec RemuxOnly, ca doit echouer plutot
        // que de surprendre l'utilisateur par une conversion soudain lente.
        var request = Request("video.mp4", "video.webm", new VideoOptions { RemuxOnly = true });

        var exception = Assert.Throws<UnsupportedConversionException>(() => _builder.Build(request, NoHardware));

        Assert.Contains("video.mp4", exception.Message, StringComparison.Ordinal);
        Assert.Contains("webm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemuxOnly_suggere_des_conteneurs_qui_fonctionneraient()
    {
        var request = Request("video.mp4", "video.webm", new VideoOptions { RemuxOnly = true });

        var exception = Assert.Throws<UnsupportedConversionException>(() => _builder.Build(request, NoHardware));

        // h264 (source par defaut dans Request) rentre dans mp4 et mkv : le message doit
        // orienter vers un conteneur qui marcherait reellement, pas juste dire « non ».
        Assert.Contains("mkv", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void N_ajoute_aucun_mappage_sans_selection_de_piste()
    {
        // Comportement par defaut, deja eprouve : ffmpeg choisit seul la premiere piste
        // de chaque type. Le changer pour tout le monde serait un risque non justifie.
        var plan = _builder.Build(Request("video.mp4", "video.mkv"), NoHardware);

        Assert.DoesNotContain("-map", plan.Arguments);
    }

    [Fact]
    public void Mappe_la_piste_audio_choisie_en_remux()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions { AudioTrackIndex = 2 });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.True(plan.IsRemux);
        Assert.Contains("-map", arguments);
        Assert.Contains("0:v:0", arguments);
        Assert.Contains("0:2", arguments);
    }

    [Fact]
    public void Mappe_la_piste_audio_choisie_en_reencodage()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            AudioTrackIndex = 2,
            Codec = VideoCodec.H265,
        });

        var plan = _builder.Build(request, NoHardware);

        Assert.False(plan.IsRemux);
        Assert.Contains("0:2", plan.Arguments);
    }

    [Fact]
    public void Mappe_une_seule_piste_de_sous_titres_quand_choisie()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions { SubtitleTrackIndex = 3 });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.Contains("0:3", arguments);

        // Une selection precise remplace le « toutes les pistes » par defaut : le
        // mappage generique des sous-titres ne doit pas cohabiter avec un choix explicite.
        Assert.DoesNotContain("0:s?", arguments);
    }

    [Fact]
    public void Mappe_toutes_les_pistes_de_sous_titres_sans_selection_precise()
    {
        // KeepSubtitles=true est le defaut : des qu'une selection de piste declenche le
        // mode mappage explicite, ce comportement « toutes » doit survivre a la bascule.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { AudioTrackIndex = 1 });

        var plan = _builder.Build(request, NoHardware);

        Assert.Contains("0:s?", plan.Arguments);
    }

    [Fact]
    public void Ajoute_une_entree_ffmpeg_par_sous_titre_externe_et_les_mappe()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            ExternalSubtitles =
            [
                new SubtitleImport { FilePath = "vostfr.srt" },
                new SubtitleImport { FilePath = "vosta.srt" },
            ],
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        // Entree principale (0) suivie d'une entree par sous-titre (1, 2), chacune mappee
        // par son propre index d'entree — pas celui de la source.
        Assert.Contains("vostfr.srt", arguments);
        Assert.Contains("vosta.srt", arguments);
        Assert.Contains("1:0", arguments);
        Assert.Contains("2:0", arguments);
    }

    [Fact]
    public void Ajoute_les_metadonnees_langue_et_titre_du_sous_titre_externe()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            ExternalSubtitles = [new SubtitleImport { FilePath = "vostfr.srt", Language = "fre", Title = "VOSTFR" }],
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.Contains("-metadata:s:s:0", arguments);
        Assert.Contains("language=fre", arguments);
        Assert.Contains("title=VOSTFR", arguments);
    }

    [Fact]
    public void Place_le_sous_titre_externe_apres_la_piste_embarquee_choisie_dans_les_metadonnees()
    {
        // Une piste embarquee explicitement choisie occupe la position s:0 : le sous-titre
        // externe qui la suit doit etre numerote s:1, pas s:0.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            SubtitleTrackIndex = 3,
            ExternalSubtitles = [new SubtitleImport { FilePath = "vostfr.srt", Language = "fre" }],
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.Contains("-metadata:s:s:1", arguments);
        Assert.DoesNotContain("-metadata:s:s:0", arguments);
    }

    [Fact]
    public void Refuse_les_sous_titres_externes_hors_mkv()
    {
        var request = Request("video.mp4", "video.mp4", new VideoOptions
        {
            ExternalSubtitles = [new SubtitleImport { FilePath = "vostfr.srt" }],
        });

        var exception = Assert.Throws<UnsupportedConversionException>(() => _builder.Build(request, NoHardware));

        Assert.Contains("mkv", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copie_toujours_les_flux_video_et_audio_avec_des_sous_titres_externes()
    {
        // Le point de la fonctionnalite : integrer un sous-titre ne doit jamais declencher
        // un reencodage de l'image ou du son quand une simple copie suffisait sinon.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            ExternalSubtitles = [new SubtitleImport { FilePath = "vostfr.srt" }],
        });

        var plan = _builder.Build(request, NoHardware);

        Assert.True(plan.IsRemux);
        Assert.Contains("copy", plan.Arguments);
    }

    [Fact]
    public void Copie_les_sous_titres_gardes_en_bloc_meme_en_reencodage()
    {
        // Regression : avant ce correctif, choisir une piste audio precise ET garder toutes
        // les pistes de sous-titres (comportement par defaut) tout en forcant un reencodage
        // laissait ffmpeg choisir son encodeur de sous-titres par defaut au lieu de copier.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            AudioTrackIndex = 1,
            Codec = VideoCodec.H265,
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.False(plan.IsRemux);
        Assert.Contains("0:s?", arguments);
        Assert.Contains("-c:s", arguments);
    }

    [Fact]
    public void N_omet_aucune_piste_audio_quand_RemoveAudio_et_une_piste_video_choisie()
    {
        // La selection de piste ne doit pas court-circuiter RemoveAudio : les deux
        // reglages sont independants.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            SubtitleTrackIndex = 1,
            RemoveAudio = true,
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.DoesNotContain("0:a:0?", arguments);
        Assert.Contains("-an", arguments);
    }

    [Fact]
    public void Choisit_l_encodeur_materiel_quand_il_fonctionne()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Codec = VideoCodec.H264 });

        var plan = _builder.Build(request, Nvenc);

        Assert.Contains("h264_nvenc", plan.Arguments);
    }

    [Fact]
    public void Retombe_en_logiciel_sans_materiel_disponible()
    {
        // Une machine sans GPU compatible doit convertir quand meme, sans erreur.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Codec = VideoCodec.H264 });

        var plan = _builder.Build(request, NoHardware);

        Assert.Contains("libx264", plan.Arguments);
        Assert.DoesNotContain("h264_nvenc", plan.Arguments);
    }

    [Fact]
    public void Retombe_en_logiciel_quand_le_materiel_demande_est_absent()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            Codec = VideoCodec.H264,
            Hardware = HardwareAcceleration.QuickSync,
        });

        var plan = _builder.Build(request, Nvenc);

        Assert.Contains("libx264", plan.Arguments);
    }

    [Fact]
    public void Precise_toujours_le_multiplexeur()
    {
        // La sortie s'ecrit dans un « .part », dont l'extension ne dit rien a ffmpeg :
        // sans -f explicite il ne saurait pas quel conteneur produire.
        var plan = _builder.Build(Request("video.mp4", "video.mkv"), NoHardware);

        var index = plan.Arguments.ToList().LastIndexOf("-f");

        Assert.True(index >= 0, "Le multiplexeur de sortie doit etre impose explicitement.");
        Assert.Equal("matroska", plan.Arguments[index + 1]);
    }

    [Fact]
    public void Ecrit_dans_un_fichier_temporaire()
    {
        var request = Request("video.mp4", "video.mkv");

        var plan = _builder.Build(request, NoHardware);

        Assert.Equal(request.WorkingPath, plan.Arguments[^1]);
        Assert.EndsWith(".part", plan.Arguments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Demande_l_avancement_lisible_par_la_machine()
    {
        var plan = _builder.Build(Request("video.mp4", "video.mkv"), NoHardware);

        Assert.Contains("-progress", plan.Arguments);
        Assert.Contains("pipe:1", plan.Arguments);
        Assert.Contains("-nostats", plan.Arguments);
    }

    [Fact]
    public void Construit_le_GIF_en_une_seule_passe()
    {
        // La variante en deux passes imposerait un fichier de palette temporaire, qu'il
        // faudrait nettoyer y compris apres une annulation.
        var request = Request("video.mp4", "video.gif", new GifOptions { FrameRate = 15, Width = 480 });

        var plan = _builder.Build(request, NoHardware);
        var filter = plan.Arguments[plan.Arguments.ToList().IndexOf("-filter_complex") + 1];

        Assert.Contains("split", filter, StringComparison.Ordinal);
        Assert.Contains("palettegen", filter, StringComparison.Ordinal);
        Assert.Contains("paletteuse", filter, StringComparison.Ordinal);
        Assert.Contains("fps=15", filter, StringComparison.Ordinal);
        Assert.Contains("scale=480:-1", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Ecarte_la_video_lors_d_une_extraction_audio()
    {
        // Sans -vn, une pochette integree serait traitee comme un flux video et ferait
        // echouer le multiplexage.
        var request = Request("video.mp4", "bande-son.mp3");

        var plan = _builder.Build(request, NoHardware);

        Assert.Contains("-vn", plan.Arguments);
        Assert.Contains("libmp3lame", plan.Arguments);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void Formate_la_cadence_independamment_de_la_culture(string culture)
    {
        // Sur une machine francaise, « 29,97 » serait rejete par ffmpeg.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var request = Request("video.mp4", "video.mkv", new VideoOptions { FrameRate = 29.97 });
            var plan = _builder.Build(request, NoHardware);

            Assert.Contains("29.97", plan.Arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Traduit_une_fin_de_decoupe_en_duree()
    {
        // -to combine a un -ss place avant l'entree n'a pas le meme sens selon les versions
        // de ffmpeg ; une duree explicite leve toute ambiguite.
        var request = Request("video.mp4", "extrait.mkv", new VideoOptions
        {
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(25),
        });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.Equal("10", arguments[arguments.IndexOf("-ss") + 1]);
        Assert.Equal("15", arguments[arguments.IndexOf("-t") + 1]);
    }

    [Fact]
    public void Positionne_le_saut_avant_le_fichier_d_entree()
    {
        // Place apres -i, ffmpeg decoderait tout depuis le debut pour jeter le resultat.
        var request = Request("video.mp4", "extrait.mkv", new VideoOptions
        {
            StartTime = TimeSpan.FromSeconds(30),
        });

        var arguments = _builder.Build(request, NoHardware).Arguments.ToList();

        Assert.True(arguments.IndexOf("-ss") < arguments.IndexOf("-i"));
    }

    [Fact]
    public void Duplique_les_images_par_defaut_pour_changer_de_cadence()
    {
        // Mode instantane : ffmpeg se contente de repeter des images. Rien de plus fluide,
        // mais rien de coûteux non plus.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { FrameRate = 120 });

        var plan = _builder.Build(request, NoHardware);
        var arguments = plan.Arguments.ToList();

        Assert.Equal("120", arguments[arguments.IndexOf("-r") + 1]);
        Assert.DoesNotContain(plan.Arguments, a => a.Contains("minterpolate", StringComparison.Ordinal));
    }

    [Fact]
    public void Interpole_reellement_quand_c_est_demande()
    {
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            FrameRate = 120,
            FrameRateMode = FrameRateMode.Interpolate,
        });

        var plan = _builder.Build(request, NoHardware);
        var filters = plan.Arguments[plan.Arguments.ToList().IndexOf("-vf") + 1];

        Assert.Contains("minterpolate=fps=120", filters, StringComparison.Ordinal);

        // mci est le seul mode qui compense le mouvement ; dup et blend se contenteraient
        // de dupliquer ou de fondre, c'est-a-dire de ne rien apporter.
        Assert.Contains("mi_mode=mci", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void N_ajoute_pas_de_cadence_de_sortie_en_mode_interpole()
    {
        // Le filtre produit deja la cadence voulue. Un -r par-dessus rejetterait ou
        // rededoublerait les images tout juste calculees, annulant le benefice.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            FrameRate = 60,
            FrameRateMode = FrameRateMode.Interpolate,
        });

        var plan = _builder.Build(request, NoHardware);

        Assert.DoesNotContain("-r", plan.Arguments);
    }

    [Fact]
    public void Redimensionne_avant_d_interpoler()
    {
        // L'estimation de mouvement est l'etape la plus coûteuse : la faire sur une image
        // deja reduite plutot que sur du 4K change le temps de traitement d'un ordre de
        // grandeur.
        var request = Request("video.mp4", "video.mkv", new VideoOptions
        {
            Width = 1280,
            FrameRate = 60,
            FrameRateMode = FrameRateMode.Interpolate,
        });

        var plan = _builder.Build(request, NoHardware);
        var filters = plan.Arguments[plan.Arguments.ToList().IndexOf("-vf") + 1];

        Assert.True(
            filters.IndexOf("scale=", StringComparison.Ordinal) <
            filters.IndexOf("minterpolate", StringComparison.Ordinal),
            $"Le redimensionnement doit preceder l'interpolation : {filters}");
    }

    [Fact]
    public void Impose_un_format_de_pixels_compatible_en_h264()
    {
        // Un profil superieur passerait sur un ordinateur et sur aucun televiseur.
        var request = Request("video.mp4", "video.mkv", new VideoOptions { Codec = VideoCodec.H264 });

        var plan = _builder.Build(request, NoHardware);

        Assert.Contains("yuv420p", plan.Arguments);
    }

    private static ConversionRequest Request(string input, string output, ConversionOptions? options = null)
    {
        var registry = FormatRegistry.Default;

        return new ConversionRequest
        {
            InputPath = input,
            OutputPath = output,
            SourceFormat = registry.GetByPath(input),
            TargetFormat = registry.GetByPath(output),
            Options = options ?? ConversionOptions.None,

            // Source h264 + aac : le cas courant, compatible mp4 comme mkv.
            SourceInfo = new MediaInfo
            {
                Duration = TimeSpan.FromMinutes(2),
                Streams =
                [
                    new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, CodecName = "h264" },
                    new MediaStreamInfo { Index = 1, Kind = MediaStreamKind.Audio, CodecName = "aac" },
                ],
            },
        };
    }
}
