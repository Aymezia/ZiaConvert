namespace ZiaConvert.Core.Options;

/// <summary>Reglages d'une conversion video (conteneur, codec, resolution, qualite).</summary>
public sealed record VideoOptions : ConversionOptions
{
    public VideoCodec Codec { get; init; } = VideoCodec.Auto;

    public AudioCodec Audio { get; init; } = AudioCodec.Auto;

    /// <summary>Largeur cible. <c>null</c> conserve celle de la source.</summary>
    public int? Width { get; init; }

    /// <summary>Hauteur cible. <c>null</c> conserve celle de la source.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// Plafond de largeur : reduit la video si elle depasse, la laisse intacte sinon.
    /// </summary>
    /// <remarks>
    /// Different de <see cref="Width" />, qui impose la dimension. Un preglage « Web » a
    /// 1920 doit reduire une source 4K sans agrandir une source 720p : agrandir ne ferait
    /// que gonfler le fichier sans ajouter le moindre detail.
    /// </remarks>
    public int? MaxWidth { get; init; }

    /// <summary>Cadence de sortie en images par seconde. <c>null</c> conserve celle de la source.</summary>
    public double? FrameRate { get; init; }

    /// <summary>
    /// Comment atteindre <see cref="FrameRate" />. Par defaut on duplique, ce qui est
    /// instantane ; l'interpolation doit rester un choix explicite, car elle transforme
    /// une conversion de quelques secondes en plusieurs minutes.
    /// </summary>
    public FrameRateMode FrameRateMode { get; init; } = FrameRateMode.Duplicate;

    /// <summary>
    /// Qualite constante (CRF pour x264/x265, CQ pour NVENC). Plus bas = meilleure qualite.
    /// Plage utile 18-28, defaut moteur si <c>null</c>.
    /// </summary>
    public int? Quality { get; init; }

    /// <summary>Debit video impose, en bits/s. Exclusif avec <see cref="Quality" />.</summary>
    public long? VideoBitrate { get; init; }

    public long? AudioBitrate { get; init; }

    public HardwareAcceleration Hardware { get; init; } = HardwareAcceleration.Auto;

    /// <summary>
    /// Autorise la copie de flux sans reencodage quand le conteneur cible accepte les codecs
    /// de la source. C'est ce qui fait passer un mp4 vers mkv de plusieurs minutes a deux secondes.
    /// </summary>
    public bool AllowRemux { get; init; } = true;

    /// <summary>
    /// Exige une copie de flux pure : la conversion echoue si le conteneur cible n'accepte
    /// pas les codecs source, plutot que de retomber silencieusement sur un reencodage.
    /// </summary>
    /// <remarks>
    /// Utile pour un rip DVD (MPEG-2, AC3) qu'on veut seulement re-emballer, sans toucher
    /// a l'image ni a la piste son : sans ce garde-fou, une simple erreur de conteneur cible
    /// (mp4 au lieu de mkv) declencherait un reencodage complet aux reglages par defaut,
    /// invisible tant qu'on n'a pas remarque le temps que ca prend.
    /// </remarks>
    public bool RemuxOnly { get; init; }

    public ScalingAlgorithm Scaling { get; init; } = ScalingAlgorithm.Lanczos;

    /// <summary>Debut du extrait a convertir. <c>null</c> part du debut du fichier.</summary>
    public TimeSpan? StartTime { get; init; }

    /// <summary>Fin de l'extrait a convertir. <c>null</c> va jusqu'a la fin du fichier.</summary>
    public TimeSpan? EndTime { get; init; }

    /// <summary>Retire la piste audio.</summary>
    public bool RemoveAudio { get; init; }

    /// <summary>Conserve les pistes de sous-titres quand le conteneur cible les accepte.</summary>
    public bool KeepSubtitles { get; init; } = true;

    /// <summary>
    /// Piste audio a conserver, par son index absolu tel qu'affiche par <c>zia probe</c>
    /// (ex. <c>[1] Audio aac ...</c> -> <c>1</c>). <c>null</c> laisse ffmpeg choisir seul,
    /// ce qui prend en general la premiere piste — insuffisant des qu'une source (rip DVD,
    /// Blu-ray multilingue) porte plusieurs langues ou une piste de commentaire.
    /// </summary>
    public int? AudioTrackIndex { get; init; }

    /// <summary>
    /// Piste de sous-titres a conserver seule, par son index absolu. <c>null</c> suit
    /// <see cref="KeepSubtitles" /> (toutes ou aucune) ; renseigne, seule cette piste est
    /// gardee, les autres sont ecartees meme si <see cref="KeepSubtitles" /> vaut vrai.
    /// </summary>
    public int? SubtitleTrackIndex { get; init; }

    /// <summary>
    /// Sous-titres externes (.srt, .ass, .ssa, .vtt) a integrer a la sortie, dans l'ordre
    /// donne. Reserve au conteneur mkv, seul a accepter ces pistes texte sans reencoder le
    /// reste : demander a la fois des sous-titres externes et un autre conteneur echoue
    /// clairement plutot que d'ignorer les fichiers en silence.
    /// </summary>
    public IReadOnlyList<SubtitleImport> ExternalSubtitles { get; init; } = [];
}
