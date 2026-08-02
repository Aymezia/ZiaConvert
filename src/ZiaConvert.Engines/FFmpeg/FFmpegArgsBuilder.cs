using System.Globalization;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Engines.FFmpeg;

/// <summary>Ligne de commande prete a executer, accompagnee de ce qu'elle va faire.</summary>
internal sealed record FFmpegPlan(IReadOnlyList<string> Arguments, bool IsRemux, string Description);

/// <summary>
/// Traduit une demande de conversion en arguments ffmpeg.
/// </summary>
/// <remarks>
/// Toute la connaissance de ffmpeg est concentree ici : le moteur se contente d'executer
/// et de lire l'avancement. C'est aussi ici que se prend la decision la plus rentable de
/// l'application, celle de copier les flux plutot que de les reencoder.
/// </remarks>
internal sealed class FFmpegArgsBuilder
{
    public FFmpegPlan Build(ConversionRequest request, HardwareSupport hardware)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (FFmpegMuxers.For(request.TargetFormat.Id) is null)
        {
            throw new UnsupportedConversionException(
                $"ffmpeg ne sait pas produire le format « {request.TargetFormat.DisplayName} ».");
        }

        return request.TargetFormat switch
        {
            { Id: "gif" } => BuildGif(request),
            { Family: FormatFamily.Audio } => BuildAudio(request),
            { Family: FormatFamily.Video } => BuildVideo(request, hardware),
            _ => throw new UnsupportedConversionException(
                $"Conversion vers « {request.TargetFormat.DisplayName} » non prise en charge par ffmpeg."),
        };
    }

    /// <summary>Options globales communes, posees avant toute lecture de fichier.</summary>
    /// <param name="regenerateTimestamps">
    /// Ajoute <c>-fflags +genpts</c> avant l'entree. Necessaire pour remuxer certaines
    /// sources aux horodatages discontinus (verifie sur un flux MPEG-2/AC3 de type rip
    /// DVD : sans ce reglage, la copie de flux vers matroska echoue avec « Can't write
    /// packet with unknown timestamp »). Cela ne modifie que la metadonnee de temps
    /// recalculee a partir du flux, pas les donnees encodees elles-memes : ca reste un
    /// remux au sens strict. Reserve au remux pour ne pas changer le comportement,
    /// non teste, du chemin de reencodage.
    /// </param>
    /// <param name="extraInputs">
    /// Fichiers de sous-titres externes, ajoutes comme entrees supplementaires (index 1,
    /// 2...) apres l'entree principale. Reçoivent le meme decoupage qu'elle : sans cela,
    /// des sous-titres externes sur un extrait decoupe partiraient du debut du fichier
    /// entier et seraient desynchronises.
    /// </param>
    private static ArgumentBuilder Prologue(
        ConversionRequest request, bool regenerateTimestamps = false, IReadOnlyList<string>? extraInputs = null)
    {
        var builder = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")

            // L'avancement machine part sur la sortie standard ; la barre destinee a
            // l'humain est coupee pour ne pas polluer la sortie d'erreur.
            .Add("-nostats")
            .Add("-progress", "pipe:1")

            // On ecrit dans un .part qui peut subsister apres une annulation brutale.
            .Add("-y")
            .AddIf(regenerateTimestamps, "-fflags", "+genpts");

        var trim = Trim(request);

        AddInput(builder, request.InputPath, trim);

        if (extraInputs is not null)
        {
            foreach (var path in extraInputs)
            {
                AddInput(builder, path, trim);
            }
        }

        return builder;
    }

    /// <summary>
    /// Positionnement avant -i : saut sur index, quasi instantane. Place apres, ffmpeg
    /// decoderait tout depuis le debut pour jeter le resultat. Une duree est moins ambigue
    /// qu'un point de fin : la signification de -to varie selon les versions quand elle
    /// est combinee a un -ss place avant l'entree.
    /// </summary>
    private static void AddInput(ArgumentBuilder builder, string path, (TimeSpan? Start, TimeSpan? Duration) trim)
    {
        if (trim.Start is { } start)
        {
            builder.Add("-ss", start);
        }

        builder.Add("-i", path);

        if (trim.Duration is { } duration)
        {
            builder.Add("-t", duration);
        }
    }

    private FFmpegPlan BuildVideo(ConversionRequest request, HardwareSupport hardware)
    {
        var options = request.Options as VideoOptions ?? new VideoOptions();
        var container = request.TargetFormat.Id;

        if (options.ExternalSubtitles.Count > 0 && container != "mkv")
        {
            // Matroska est le seul conteneur courant a accepter du texte (subrip/ass) tel
            // quel : mp4 exigerait une conversion vers mov_text, webm n'accepte pas subrip
            // du tout. Plutot que de deviner, on refuse clairement.
            throw new UnsupportedConversionException(
                $"Les sous-titres externes ne peuvent etre integres que dans un conteneur mkv, pas {container}.");
        }

        var canRemux = TryRemux(request, options, out var reason);

        if (!canRemux && options.RemuxOnly)
        {
            // Une demande de remux explicite doit echouer clairement plutot que de
            // basculer sans prevenir sur un reencodage complet aux reglages par defaut,
            // invisible tant qu'on n'a pas remarque le temps que ca prend.
            throw new UnsupportedConversionException(BuildRemuxOnlyFailure(request, container, reason));
        }

        var mapping = BuildStreamMapping(options);
        var subtitleFiles = options.ExternalSubtitles.Count > 0
            ? options.ExternalSubtitles.Select(s => s.FilePath).ToList()
            : null;

        if (canRemux)
        {
            var remuxBuilder = Prologue(request, regenerateTimestamps: true, extraInputs: subtitleFiles);

            if (mapping is not null)
            {
                // Une fois la selection explicite, -c copy en tete suffit a copier le
                // codec de tout ce qui est mappe (video, audio choisie, sous-titre choisi,
                // sous-titres externes) : pas besoin d'un -c:s copy separe comme dans le
                // cas par defaut ci-dessous.
                remuxBuilder.AddRange(mapping);
            }

            remuxBuilder
                .Add("-c", "copy")

                // Sans cela, un MP4 issu d'un MKV garde des horodatages qui commencent
                // parfois loin de zero, ce que certains lecteurs interpretent mal.
                .Add("-avoid_negative_ts", "make_zero")
                .AddIf(mapping is null && options.KeepSubtitles && container is "mkv" or "mp4", "-c:s", "copy")
                .AddIf(mapping is null && !options.KeepSubtitles, "-sn")
                .Add("-f", FFmpegMuxers.For(container)!)
                .Add(request.WorkingPath);

            return new FFmpegPlan(remuxBuilder.Build(), IsRemux: true, "Copie des flux sans reencodage");
        }

        var codec = ResolveVideoCodec(options.Codec, container);
        var encoder = hardware.ResolveEncoder(codec, options.Hardware);
        var builder = Prologue(request, extraInputs: subtitleFiles);

        if (mapping is not null)
        {
            builder.AddRange(mapping);
        }

        if (BuildVideoFilters(options) is { } filters)
        {
            builder.Add("-vf", filters);
        }

        builder.Add("-c:v", encoder);
        ApplyQuality(builder, encoder, options);

        // En interpolation, la cadence est produite par le filtre lui-meme : ajouter -r
        // en plus ferait redupliquer ou jeter les images tout juste calculees.
        if (options.FrameRate is { } frameRate && options.FrameRateMode == FrameRateMode.Duplicate)
        {
            builder.Add("-r", frameRate);
        }

        // H.264 en 4:2:0 8 bits : c'est ce que savent decoder les televiseurs et les
        // telephones. Un profil superieur passerait sur un ordinateur et nulle part ailleurs.
        if (codec == VideoCodec.H264)
        {
            builder.Add("-pix_fmt", "yuv420p");
        }

        ApplyAudio(builder, options, container);

        // Toute piste de sous-titres mappee (choisie, gardee en bloc, ou externe) doit
        // rester une copie : sans -c:s explicite ici, ffmpeg choisirait son encodeur de
        // sous-titres par defaut pour le reencodage, qui ne correspond pas forcement au
        // format d'origine.
        var hasMappedSubtitles = mapping is not null &&
            (options.SubtitleTrackIndex is not null || options.ExternalSubtitles.Count > 0 || options.KeepSubtitles);

        builder
            .AddIf(hasMappedSubtitles, "-c:s", "copy")
            .AddIf(mapping is null && !options.KeepSubtitles, "-sn")
            .Add("-f", FFmpegMuxers.For(container)!)
            .Add(request.WorkingPath);

        var description = encoder.Contains("nvenc", StringComparison.Ordinal) ||
                          encoder.Contains("qsv", StringComparison.Ordinal) ||
                          encoder.Contains("amf", StringComparison.Ordinal)
            ? $"Reencodage {encoder} (materiel)"
            : $"Reencodage {encoder} (logiciel) — {reason}";

        return new FFmpegPlan(builder.Build(), IsRemux: false, description);
    }

    private static FFmpegPlan BuildAudio(ConversionRequest request)
    {
        var options = request.Options as AudioOptions ?? new AudioOptions();
        var container = request.TargetFormat.Id;
        var codec = ResolveAudioCodec(options.Codec, container);

        var builder = Prologue(request)

            // La source est souvent une video : on ne garde que le son, et on ecarte les
            // pochettes integrees, que ffmpeg traiterait sinon comme un flux video.
            .Add("-vn")
            .Add("-c:a", codec);

        if (options.Bitrate is { } bitrate && !IsLossless(codec))
        {
            builder.Add("-b:a", bitrate);
        }

        builder
            .AddIfNotNull("-ar", options.SampleRate)
            .AddIfNotNull("-ac", options.Channels)
            .AddIf(options.Normalize, "-af", "loudnorm=I=-16:TP=-1.5:LRA=11")
            .Add("-f", FFmpegMuxers.For(container)!)
            .Add(request.WorkingPath);

        return new FFmpegPlan(builder.Build(), IsRemux: false, $"Extraction audio {codec}");
    }

    /// <summary>
    /// Construit la conversion vers GIF.
    /// </summary>
    /// <remarks>
    /// Le GIF est limite a 256 couleurs, et la palette par defaut de ffmpeg donne un
    /// resultat mediocre. On calcule donc une palette adaptee au contenu. Le filtre
    /// <c>split</c> permet de le faire en une seule passe : la variante classique en deux
    /// passes imposerait un fichier de palette temporaire a nettoyer, y compris apres une
    /// annulation.
    /// </remarks>
    private static FFmpegPlan BuildGif(ConversionRequest request)
    {
        var options = request.Options as GifOptions ?? new GifOptions();

        var scale = options.Width is { } width
            ? $",scale={width.ToString(CultureInfo.InvariantCulture)}:-1:flags=lanczos"
            : string.Empty;

        var paletteMode = options.DiffPalette ? "=stats_mode=diff" : string.Empty;
        var dither = options.Dither switch
        {
            DitherMode.None => "dither=none",
            DitherMode.Bayer => $"dither=bayer:bayer_scale={options.BayerScale.ToString(CultureInfo.InvariantCulture)}",
            DitherMode.FloydSteinberg => "dither=floyd_steinberg",
            DitherMode.Sierra2 => "dither=sierra2_4a",
            _ => "dither=bayer",
        };

        var fps = options.FrameRate.ToString("0.###", CultureInfo.InvariantCulture);
        var filter =
            $"fps={fps}{scale},split[a][b];[a]palettegen{paletteMode}[p];[b][p]paletteuse={dither}";

        var arguments = Prologue(request)
            .Add("-filter_complex", filter)
            .Add("-loop", options.Loop ? 0 : -1)
            .Add("-f", "gif")
            .Add(request.WorkingPath)
            .Build();

        return new FFmpegPlan(arguments, IsRemux: false, "Conversion GIF avec palette adaptee");
    }

    /// <summary>
    /// Decide si la conversion peut se limiter a une reecriture du conteneur.
    /// </summary>
    /// <param name="reason">Motif du reencodage, a afficher a l'utilisateur.</param>
    private static bool TryRemux(ConversionRequest request, VideoOptions options, out string reason)
    {
        if (!options.AllowRemux)
        {
            reason = "copie de flux desactivee";
            return false;
        }

        if (options.Codec is not (VideoCodec.Auto or VideoCodec.Copy))
        {
            reason = "un codec video precis a ete demande";
            return false;
        }

        // Le moindre filtre impose de decoder puis reencoder : redimensionner ou changer
        // la cadence est incompatible avec une simple copie.
        if (options.Width is not null ||
            options.Height is not null ||
            options.MaxWidth is not null ||
            options.FrameRate is not null)
        {
            reason = "la video doit etre redimensionnee ou recadencee";
            return false;
        }

        if (options.Quality is not null || options.VideoBitrate is not null)
        {
            reason = "une qualite ou un debit precis a ete demande";
            return false;
        }

        if (options.RemoveAudio || options.Audio is not (AudioCodec.Auto or AudioCodec.Copy))
        {
            reason = "la piste audio doit etre modifiee";
            return false;
        }

        if (request.SourceInfo is null)
        {
            reason = "le fichier source n'a pas pu etre analyse";
            return false;
        }

        if (!ContainerCompatibility.CanRemux(request.TargetFormat.Id, request.SourceInfo, out var incompatibility))
        {
            reason = incompatibility ?? "les codecs source ne sont pas compatibles avec le conteneur cible";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Compose un message explicite pour un remux impossible, en suggerant les
    /// conteneurs qui accepteraient les codecs source tels quels.
    /// </summary>
    private static string BuildRemuxOnlyFailure(ConversionRequest request, string container, string reason)
    {
        var file = Path.GetFileName(request.InputPath);
        var alternatives = request.SourceInfo is { } info
            ? SuggestRemuxableContainers(info).ToList()
            : [];

        var suggestion = alternatives.Count > 0
            ? $" Essayez plutot : {string.Join(", ", alternatives)}."
            : " Aucun conteneur courant n'accepte ces codecs sans reencodage.";

        // Les motifs issus de ContainerCompatibility se terminent deja par un point,
        // ceux issus des verifications ci-dessus non : on normalise avant d'assembler.
        var cleanReason = reason.TrimEnd('.');

        return $"« {file} » ne peut pas etre remuxe vers {container} : {cleanReason}.{suggestion}";
    }

    /// <summary>Conteneurs usuels qui accepteraient les codecs de la source sans reencodage.</summary>
    private static IEnumerable<string> SuggestRemuxableContainers(MediaInfo source) =>
        new[] { "mkv", "mp4", "mov", "webm", "ts" }
            .Where(candidate => ContainerCompatibility.CanRemux(candidate, source, out _));

    /// <summary>
    /// Assemble la chaine de filtres video.
    /// </summary>
    /// <remarks>
    /// L'ordre compte : on redimensionne avant d'interpoler. L'estimation de mouvement
    /// est de loin l'operation la plus couteuse, et la faire sur une image reduite plutot
    /// que sur du 4K change le temps de traitement d'un ordre de grandeur.
    /// </remarks>
    private static string? BuildVideoFilters(VideoOptions options)
    {
        var filters = new List<string>();

        if (BuildScaleFilter(options) is { } scale)
        {
            filters.Add(scale);
        }

        if (BuildFrameRateFilter(options) is { } frameRate)
        {
            filters.Add(frameRate);
        }

        return filters.Count > 0 ? string.Join(',', filters) : null;
    }

    /// <summary>
    /// Construit le filtre de cadence, uniquement en mode interpole.
    /// </summary>
    /// <remarks>
    /// Passer de 60 a 120 images par seconde en dupliquant chaque image ne rend rien plus
    /// fluide : le fichier annonce 120 im/s et se regarde exactement comme du 60. La
    /// fluidite reelle demande de fabriquer les images intermediaires, ce que fait
    /// <c>minterpolate</c> par estimation de mouvement — lentement, et avec des artefacts
    /// possibles sur les mouvements rapides.
    /// <para>
    /// <c>mi_mode=mci</c> est le seul mode qui compense reellement le mouvement ;
    /// <c>dup</c> et <c>blend</c> se contentent de dupliquer ou de fondre. La detection de
    /// changement de plan reste active par defaut : sans elle, chaque coupe produirait une
    /// image de transition difforme entre deux plans sans rapport.
    /// </para>
    /// </remarks>
    private static string? BuildFrameRateFilter(VideoOptions options)
    {
        if (options.FrameRate is not { } target || options.FrameRateMode != FrameRateMode.Interpolate)
        {
            return null;
        }

        var fps = target.ToString("0.###", CultureInfo.InvariantCulture);

        return $"minterpolate=fps={fps}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1";
    }

    private static string? BuildScaleFilter(VideoOptions options)
    {
        if (options.Width is null && options.Height is null && options.MaxWidth is null)
        {
            return null;
        }

        var algorithm = options.Scaling switch
        {
            ScalingAlgorithm.Lanczos => "lanczos",
            ScalingAlgorithm.Bicubic => "bicubic",
            ScalingAlgorithm.Bilinear => "bilinear",
            ScalingAlgorithm.Neighbor => "neighbor",
            ScalingAlgorithm.Spline => "spline",
            _ => "lanczos",
        };

        // Plafond de largeur : min(cible, largeur source) laisse passer les videos deja
        // plus petites sans les agrandir. Le resultat est arrondi au nombre pair inferieur,
        // les encodeurs 4:2:0 refusant les dimensions impaires.
        if (options.MaxWidth is { } maximum)
        {
            var cap = maximum.ToString(CultureInfo.InvariantCulture);
            return $"scale='min({cap},iw)':-2:flags={algorithm}";
        }

        // -2 laisse ffmpeg deduire la dimension manquante en conservant le ratio, tout en
        // l'arrondissant a un nombre pair.
        var width = options.Width?.ToString(CultureInfo.InvariantCulture) ?? "-2";
        var height = options.Height?.ToString(CultureInfo.InvariantCulture) ?? "-2";

        return $"scale={width}:{height}:flags={algorithm}";
    }

    /// <summary>
    /// Applique la consigne de qualite. Chaque famille d'encodeur a sa propre echelle et
    /// ses propres options : un <c>-crf</c> passe a NVENC est ignore en silence.
    /// </summary>
    private static void ApplyQuality(ArgumentBuilder builder, string encoder, VideoOptions options)
    {
        if (options.VideoBitrate is { } bitrate)
        {
            builder.Add("-b:v", bitrate);
            return;
        }

        var quality = options.Quality;

        if (encoder.EndsWith("_nvenc", StringComparison.Ordinal))
        {
            builder
                .Add("-preset", "p5")
                .Add("-rc", "vbr")
                .Add("-cq", quality ?? 23)

                // Sans debit cible nul, NVENC ignore la consigne de qualite constante.
                .Add("-b:v", 0);
        }
        else if (encoder.EndsWith("_qsv", StringComparison.Ordinal))
        {
            builder.Add("-global_quality", quality ?? 23);
        }
        else if (encoder.EndsWith("_amf", StringComparison.Ordinal))
        {
            builder
                .Add("-rc", "cqp")
                .Add("-qp_i", quality ?? 23)
                .Add("-qp_p", quality ?? 23);
        }
        else if (encoder == "libvpx-vp9")
        {
            builder.Add("-crf", quality ?? 31).Add("-b:v", 0);
        }
        else if (encoder == "libsvtav1")
        {
            builder.Add("-crf", quality ?? 35).Add("-preset", 8);
        }
        else if (encoder == "prores_ks")
        {
            builder.Add("-profile:v", 3);
        }
        else
        {
            builder.Add("-crf", quality ?? 23).Add("-preset", "medium");
        }
    }

    /// <summary>
    /// Construit une selection explicite de flux pour une source a plusieurs pistes audio
    /// ou sous-titres (rip DVD/Blu-ray multilingue, piste de commentaire...).
    /// </summary>
    /// <remarks>
    /// Rend <c>null</c> quand aucune selection n'est demandee : ffmpeg garde alors son
    /// choix automatique habituel (premiere piste de chaque type), un comportement deja
    /// eprouve qu'il serait risque de changer pour tout le monde au passage.
    /// </remarks>
    private static IReadOnlyList<string>? BuildStreamMapping(VideoOptions options)
    {
        if (options.AudioTrackIndex is null && options.SubtitleTrackIndex is null && options.ExternalSubtitles.Count == 0)
        {
            return null;
        }

        // La video n'a pas d'index a choisir ici : une source a plusieurs pistes video
        // (angles multiples) reste hors champ, comme le reste du moteur aujourd'hui.
        var maps = new List<string> { "-map", "0:v:0" };

        if (!options.RemoveAudio)
        {
            maps.Add("-map");

            // « ? » rend le flux facultatif : sans lui, une source qui n'aurait pas de
            // piste audio du tout ferait echouer ffmpeg sur un -map introuvable.
            maps.Add(options.AudioTrackIndex is { } audioIndex
                ? $"0:{audioIndex.ToString(CultureInfo.InvariantCulture)}"
                : "0:a:0?");
        }

        var embeddedSubtitleMapped = false;

        if (options.SubtitleTrackIndex is { } subtitleIndex)
        {
            maps.Add("-map");
            maps.Add($"0:{subtitleIndex.ToString(CultureInfo.InvariantCulture)}");
            embeddedSubtitleMapped = true;
        }
        else if (options.ExternalSubtitles.Count == 0 && options.KeepSubtitles)
        {
            // Le bloc « toutes les pistes existantes » n'est permis que seul : en ajouter
            // par-dessus des sous-titres externes, il faudrait numeroter ces derniers
            // derriere un total de pistes embarquees inconnu a l'avance.
            maps.Add("-map");
            maps.Add("0:s?");
        }

        for (var i = 0; i < options.ExternalSubtitles.Count; i++)
        {
            // Fichier externe i : entree ffmpeg d'index (i + 1), l'entree 0 etant la source.
            maps.Add("-map");
            maps.Add($"{(i + 1).ToString(CultureInfo.InvariantCulture)}:0");

            var import = options.ExternalSubtitles[i];
            var outputIndex = (embeddedSubtitleMapped ? 1 : 0) + i;
            var specifier = $"-metadata:s:s:{outputIndex.ToString(CultureInfo.InvariantCulture)}";

            if (import.Language is { Length: > 0 } language)
            {
                maps.Add(specifier);
                maps.Add($"language={language}");
            }

            if (import.Title is { Length: > 0 } title)
            {
                maps.Add(specifier);
                maps.Add($"title={title}");
            }
        }

        return maps;
    }

    private static void ApplyAudio(ArgumentBuilder builder, VideoOptions options, string container)
    {
        if (options.RemoveAudio)
        {
            builder.Add("-an");
            return;
        }

        if (options.Audio is AudioCodec.Copy)
        {
            builder.Add("-c:a", "copy");
            return;
        }

        var codec = ResolveAudioCodec(options.Audio, container);
        builder.Add("-c:a", codec);

        if (options.AudioBitrate is { } bitrate && !IsLossless(codec))
        {
            builder.Add("-b:a", bitrate);
        }
    }

    /// <summary>Choisit un codec video adapte au conteneur quand l'utilisateur n'en impose pas.</summary>
    private static VideoCodec ResolveVideoCodec(VideoCodec requested, string container)
    {
        if (requested is not (VideoCodec.Auto or VideoCodec.Copy))
        {
            return requested;
        }

        return container switch
        {
            "webm" => VideoCodec.Vp9,
            "ogv" => VideoCodec.Vp9,
            _ => VideoCodec.H264,
        };
    }

    private static string ResolveAudioCodec(AudioCodec requested, string container)
    {
        if (requested is not (AudioCodec.Auto or AudioCodec.Copy))
        {
            return requested switch
            {
                AudioCodec.Aac => "aac",
                AudioCodec.Mp3 => "libmp3lame",
                AudioCodec.Opus => "libopus",
                AudioCodec.Flac => "flac",
                AudioCodec.Vorbis => "libvorbis",
                AudioCodec.Pcm => "pcm_s16le",
                _ => "aac",
            };
        }

        // Le conteneur decide : un OGG n'accepte pas d'AAC, un WAV veut du PCM.
        return container switch
        {
            "mp3" => "libmp3lame",
            "flac" => "flac",
            "wav" => "pcm_s16le",
            "aiff" => "pcm_s16be",
            "opus" => "libopus",
            "ogg" or "ogv" => "libvorbis",
            "webm" => "libopus",
            "wma" => "wmav2",
            _ => "aac",
        };
    }

    private static bool IsLossless(string codec) =>
        codec is "flac" or "alac" || codec.StartsWith("pcm_", StringComparison.Ordinal);

    private static (TimeSpan? Start, TimeSpan? Duration) Trim(ConversionRequest request)
    {
        var (start, end) = request.Options switch
        {
            VideoOptions video => (video.StartTime, video.EndTime),
            AudioOptions audio => (audio.StartTime, audio.EndTime),
            GifOptions gif => (gif.StartTime, gif.EndTime),
            _ => (null, null),
        };

        if (end is null)
        {
            return (start, null);
        }

        var duration = end.Value - (start ?? TimeSpan.Zero);

        return (start, duration > TimeSpan.Zero ? duration : null);
    }
}
