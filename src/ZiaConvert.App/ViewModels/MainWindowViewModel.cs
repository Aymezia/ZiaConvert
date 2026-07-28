using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZiaConvert.Core.Jobs;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Engines;

namespace ZiaConvert.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ConversionServices _services = ConversionServices.Create();
    private readonly Dictionary<Guid, FileEntryViewModel> _byJob = [];
    private readonly JobQueue _queue;

    // ---------------------------------------------------------------- Sortie
    [ObservableProperty]
    private MediaFormat? _selectedTarget;

    [ObservableProperty]
    private ConversionPreset _selectedPreset = ConversionPreset.All[0];

    [ObservableProperty]
    private bool _overwrite;

    // ---------------------------------------------------------------- Video
    [ObservableProperty]
    private CodecChoice _codec = CodecChoice.All[0];

    [ObservableProperty]
    private double _quality = 23d;

    [ObservableProperty]
    private HardwareChoice _hardware = HardwareChoice.All[0];

    [ObservableProperty]
    private bool _allowRemux = true;

    [ObservableProperty]
    private bool _keepSubtitles = true;

    // ------------------------------------------------------------ Resolution
    [ObservableProperty]
    private ResolutionChoice _resolution = ResolutionChoice.All[0];

    [ObservableProperty]
    private string _customWidth = string.Empty;

    [ObservableProperty]
    private string _customHeight = string.Empty;

    [ObservableProperty]
    private ScalingChoice _scaling = ScalingChoice.All[0];

    // --------------------------------------------------------------- Cadence
    [ObservableProperty]
    private FrameRateChoice _frameRate = FrameRateChoice.All[0];

    [ObservableProperty]
    private string _customFrameRate = string.Empty;

    // Nommee differemment du type ZiaConvert.Core.Options.FrameRateMode : les deux
    // seraient sinon indiscernables a l'usage puisque ce dernier est importe ci-dessus.
    [ObservableProperty]
    private FrameRateModeChoice _frameRateStrategy = FrameRateModeChoice.All[0];

    // ----------------------------------------------------------------- Audio
    [ObservableProperty]
    private AudioCodecChoice _audioCodec = AudioCodecChoice.All[0];

    [ObservableProperty]
    private AudioBitrateChoice _audioBitrate = AudioBitrateChoice.All[0];

    [ObservableProperty]
    private ChannelChoice _audioChannels = ChannelChoice.All[0];

    [ObservableProperty]
    private bool _normalizeAudio;

    [ObservableProperty]
    private bool _removeAudio;

    // --------------------------------------------------------------- Decoupe
    [ObservableProperty]
    private string _trimStart = string.Empty;

    [ObservableProperty]
    private string _trimEnd = string.Empty;

    // ------------------------------------------------------------------- GIF
    [ObservableProperty]
    private string _gifFrameRate = "15";

    [ObservableProperty]
    private string _gifWidth = "480";

    [ObservableProperty]
    private DitherChoice _gifDither = DitherChoice.All[0];

    // ------------------------------------------------------------------ Etat
    [ObservableProperty]
    private string _hardwareStatus = "Detection du materiel...";

    [ObservableProperty]
    private bool _hasHardware;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _summary = "Aucun fichier";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _queuedCount;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _succeededCount;

    [ObservableProperty]
    private int _failedCount;

    public MainWindowViewModel()
    {
        _queue = _services.CreateQueue();
        _queue.JobChanged += OnJobChanged;

        ApplyPreset(SelectedPreset);
        _ = DetectHardwareAsync();
    }

    public ObservableCollection<FileEntryViewModel> Files { get; } = [];

    public ObservableCollection<MediaFormat> Targets { get; } = [];

    public IReadOnlyList<ConversionPreset> Presets => ConversionPreset.All;

    public IReadOnlyList<CodecChoice> Codecs => CodecChoice.All;

    public IReadOnlyList<HardwareChoice> HardwareModes => HardwareChoice.All;

    public IReadOnlyList<ResolutionChoice> Resolutions => ResolutionChoice.All;

    public IReadOnlyList<ScalingChoice> ScalingModes => ScalingChoice.All;

    public IReadOnlyList<FrameRateChoice> FrameRates => FrameRateChoice.All;

    public IReadOnlyList<FrameRateModeChoice> FrameRateModes => FrameRateModeChoice.All;

    public IReadOnlyList<AudioCodecChoice> AudioCodecs => AudioCodecChoice.All;

    public IReadOnlyList<AudioBitrateChoice> AudioBitrates => AudioBitrateChoice.All;

    public IReadOnlyList<ChannelChoice> ChannelModes => ChannelChoice.All;

    public IReadOnlyList<DitherChoice> DitherModes => DitherChoice.All;

    public bool HasFiles => Files.Count > 0;

    public bool IsCustomResolution => Resolution.IsCustom;

    public bool IsCustomFrameRate => FrameRate.IsCustom;

    /// <summary>Vrai des qu'une cadence est imposee : la methode n'a de sens que dans ce cas.</summary>
    public bool ChangesFrameRate => FrameRate.Value is not null || FrameRate.IsCustom;

    public bool IsInterpolating => FrameRateStrategy.Value == FrameRateMode.Interpolate;

    public bool IsVideoTarget => SelectedTarget?.Family == FormatFamily.Video;

    public bool IsAudioTarget => SelectedTarget?.Family == FormatFamily.Audio;

    public bool IsGifTarget => SelectedTarget?.Id == "gif";

    /// <summary>Traduit la valeur de qualite en une phrase utile a qui ignore ce qu'est un CRF.</summary>
    public string QualityDescription => Quality switch
    {
        <= 16d => "Quasiment sans perte — fichier tres volumineux",
        <= 20d => "Haute qualite — difference invisible a l'œil",
        <= 24d => "Bon equilibre — recommande",
        <= 28d => "Fichier leger — quelques details perdus",
        _ => "Tres compresse — artefacts visibles",
    };

    /// <summary>Ajoute des fichiers, en ignorant ceux dont le format n'est pas au catalogue.</summary>
    /// <returns>Le nombre de fichiers rejetes.</returns>
    public int AddFiles(IEnumerable<string> paths)
    {
        var rejected = 0;

        foreach (var path in paths)
        {
            if (!File.Exists(path) ||
                Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var format = _services.Formats.FindByPath(path);

            if (format is null)
            {
                rejected++;
                continue;
            }

            Files.Add(new FileEntryViewModel(path, format));
        }

        RefreshTargets();
        LabelPendingFiles();
        UpdateSummary();
        OnPropertyChanged(nameof(HasFiles));

        return rejected;
    }

    public void Shutdown()
    {
        _queue.CancelAll();
        _queue.JobChanged -= OnJobChanged;

        // Attente breve et bornee : on laisse ffmpeg fermer proprement ses fichiers, sans
        // pour autant retarder la fermeture de la fenetre si un processus s'obstine.
        _queue.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(6));
    }

    [RelayCommand]
    private void Convert()
    {
        if (SelectedTarget is not { } target)
        {
            return;
        }

        foreach (var entry in Files.Where(f => f.IsPending).ToList())
        {
            // Deja au bon format : ce n'est pas une erreur, il n'y a simplement rien a faire.
            if (string.Equals(entry.SourceFormat.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            {
                entry.MarkSkipped($"Deja au format {target.Id}");
                continue;
            }

            if (!_services.Formats.TargetsFor(entry.SourceFormat).Any(t => t.Id == target.Id))
            {
                entry.MarkSkipped($"Conversion impossible depuis {entry.SourceFormat.Id}");
                continue;
            }

            var request = _services.Router.CreateRequest(
                entry.Path,
                BuildOutputPath(entry.Path, target),
                BuildOptions(target),
                Overwrite);

            var job = _queue.Enqueue(request);

            _byJob[job.Id] = entry;
            entry.Attach(job);
        }

        UpdateSummary();
    }

    [RelayCommand]
    private void CancelAll() => _queue.CancelAll();

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var entry in Files.Where(f => f.IsFinished).ToList())
        {
            if (entry.JobId is { } id)
            {
                _byJob.Remove(id);
            }

            Files.Remove(entry);
        }

        AfterListChanged();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _queue.CancelAll();
        Files.Clear();
        _byJob.Clear();

        AfterListChanged();
    }

    // ------------------------------------------------------------ Reactions

    partial void OnSelectedPresetChanged(ConversionPreset value) => ApplyPreset(value);

    partial void OnQualityChanged(double value) => OnPropertyChanged(nameof(QualityDescription));

    partial void OnResolutionChanged(ResolutionChoice value) => OnPropertyChanged(nameof(IsCustomResolution));

    partial void OnFrameRateChanged(FrameRateChoice value)
    {
        OnPropertyChanged(nameof(IsCustomFrameRate));
        OnPropertyChanged(nameof(ChangesFrameRate));
    }

    partial void OnFrameRateStrategyChanged(FrameRateModeChoice value) => OnPropertyChanged(nameof(IsInterpolating));

    partial void OnSelectedTargetChanged(MediaFormat? value)
    {
        LabelPendingFiles();

        OnPropertyChanged(nameof(IsVideoTarget));
        OnPropertyChanged(nameof(IsAudioTarget));
        OnPropertyChanged(nameof(IsGifTarget));
    }

    /// <summary>
    /// Recopie un preglage dans les champs du panneau, qui restent tous modifiables.
    /// </summary>
    private void ApplyPreset(ConversionPreset preset)
    {
        Codec = CodecChoice.All.First(c => c.Value == preset.Codec);
        Quality = preset.Quality;
        AllowRemux = preset.AllowRemux;
        AudioCodec = AudioCodecChoice.All.First(c => c.Value == preset.Audio);

        AudioBitrate = AudioBitrateChoice.All.FirstOrDefault(b => b.Value == preset.AudioBitrate)
            ?? AudioBitrateChoice.All[0];

        Resolution = ResolutionChoice.All.FirstOrDefault(r => r.Height == preset.Height)
            ?? ResolutionChoice.All[0];
    }

    // -------------------------------------------------------------- Reglages

    private ConversionOptions BuildOptions(MediaFormat target)
    {
        if (target.Id == "gif")
        {
            return new GifOptions
            {
                FrameRate = ParseDouble(GifFrameRate) ?? 15d,
                Width = ParseInt(GifWidth),
                Dither = GifDither.Value,
                StartTime = ParseTime(TrimStart),
                EndTime = ParseTime(TrimEnd),
            };
        }

        if (target.Family == FormatFamily.Audio)
        {
            return new AudioOptions
            {
                Codec = AudioCodec.Value,
                Bitrate = AudioBitrate.Value,
                Channels = AudioChannels.Value,
                Normalize = NormalizeAudio,
                StartTime = ParseTime(TrimStart),
                EndTime = ParseTime(TrimEnd),
            };
        }

        var (width, height) = ResolveResolution();

        return new VideoOptions
        {
            Codec = Codec.Value,
            Quality = (int)Math.Round(Quality),
            Hardware = Hardware.Value,
            AllowRemux = AllowRemux,
            KeepSubtitles = KeepSubtitles,
            Width = width,
            Height = height,
            Scaling = Scaling.Value,
            FrameRate = ResolveFrameRate(),
            FrameRateMode = FrameRateStrategy.Value,
            Audio = AudioCodec.Value,
            AudioBitrate = AudioBitrate.Value,
            RemoveAudio = RemoveAudio,
            StartTime = ParseTime(TrimStart),
            EndTime = ParseTime(TrimEnd),
        };
    }

    private (int? Width, int? Height) ResolveResolution()
    {
        if (Resolution.IsCustom)
        {
            return (ParseInt(CustomWidth), ParseInt(CustomHeight));
        }

        // Seule la hauteur est imposee : la largeur suit le rapport d'origine, ce qui
        // evite de deformer une source qui ne serait pas en 16:9.
        return (null, Resolution.Height);
    }

    private double? ResolveFrameRate() =>
        FrameRate.IsCustom ? ParseDouble(CustomFrameRate) : FrameRate.Value;

    // ---------------------------------------------------------------- Sortie

    /// <summary>
    /// Determine le chemin de sortie : meme dossier, meme nom, nouvelle extension.
    /// </summary>
    /// <remarks>
    /// Deux protections indispensables : ne jamais ecrire sur le fichier source, et ne pas
    /// ecraser un fichier existant sans y avoir ete autorise.
    /// </remarks>
    private string BuildOutputPath(string input, MediaFormat target)
    {
        var directory = Path.GetDirectoryName(input) ?? ".";
        var name = Path.GetFileNameWithoutExtension(input);
        var candidate = Path.Combine(directory, name + target.PrimaryExtension);

        var collidesWithSource = string.Equals(candidate, input, StringComparison.OrdinalIgnoreCase);

        if (!collidesWithSource && (Overwrite || !File.Exists(candidate)))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            var suffixed = Path.Combine(
                directory,
                string.Create(CultureInfo.InvariantCulture, $"{name} ({index}){target.PrimaryExtension}"));

            if (!File.Exists(suffixed) &&
                !string.Equals(suffixed, input, StringComparison.OrdinalIgnoreCase))
            {
                return suffixed;
            }
        }

        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N")[..6] + target.PrimaryExtension);
    }

    /// <summary>
    /// Recalcule les sorties proposables : la reunion de ce que chaque fichier depose
    /// peut produire.
    /// </summary>
    /// <remarks>
    /// L'intersection semblait plus rigoureuse, mais elle rend le cas le plus banal
    /// inutilisable : un lot melant un .mp4 et un .mkv perd a la fois mp4 et mkv, chacun
    /// etant exclu comme cible de lui-meme. Il ne restait que des formats exotiques.
    /// Les fichiers deja au format demande sont simplement ecartes au lancement.
    /// </remarks>
    private void RefreshTargets()
    {
        var previous = SelectedTarget?.Id;
        var available = new List<MediaFormat>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Files.Select(f => f.SourceFormat).DistinctBy(f => f.Id))
        {
            foreach (var target in _services.Formats.TargetsFor(source))
            {
                if (seen.Add(target.Id))
                {
                    available.Add(target);
                }
            }
        }

        Targets.Clear();

        foreach (var format in available.OrderBy(f => f.Family).ThenBy(f => f.Id, StringComparer.Ordinal))
        {
            Targets.Add(format);
        }

        // Un convertisseur qui propose « 3GPP » a l'ouverture donne l'impression d'un
        // outil mal regle : on vise les formats que les gens attendent reellement.
        SelectedTarget = Targets.FirstOrDefault(t => t.Id == previous)
            ?? Targets.FirstOrDefault(t => t.Id == "mkv")
            ?? Targets.FirstOrDefault(t => t.Id == "mp4")
            ?? Targets.FirstOrDefault(t => t.Family == FormatFamily.Video)
            ?? Targets.FirstOrDefault();
    }

    private void AfterListChanged()
    {
        RefreshTargets();
        LabelPendingFiles();
        UpdateSummary();
        OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>Affiche la cible sur chaque ligne des le depot, sans attendre le lancement.</summary>
    private void LabelPendingFiles()
    {
        foreach (var entry in Files.Where(f => f.IsPending))
        {
            entry.TargetLabel = SelectedTarget?.Id ?? "?";
        }
    }

    private async Task DetectHardwareAsync()
    {
        try
        {
            var support = await _services.Hardware.DetectAsync().ConfigureAwait(true);

            HasHardware = support.HasAnyHardware;
            HardwareStatus = support.HasAnyHardware
                ? $"{support.Preferred} — {support.WorkingEncoders.Count} encodeurs materiels"
                : "Encodage logiciel";
        }
#pragma warning disable CA1031 // Un echec de detection ne doit pas empecher l'application de servir.
        catch (Exception)
        {
            HardwareStatus = "Materiel non detecte";
            HasHardware = false;
        }
#pragma warning restore CA1031
    }

    private void OnJobChanged(object? sender, ConversionJob job)
    {
        // Les workers de la file s'executent hors du fil de l'interface : toute mise a
        // jour liee doit repasser par le repartiteur.
        Dispatcher.UIThread.Post(() =>
        {
            if (_byJob.TryGetValue(job.Id, out var entry))
            {
                entry.Refresh();
            }

            UpdateSummary();
        });
    }

    private void UpdateSummary()
    {
        TotalCount = Files.Count;
        QueuedCount = Files.Count(f => f.IsQueued);
        RunningCount = Files.Count(f => f.IsRunning);
        SucceededCount = Files.Count(f => f.IsSucceeded);
        FailedCount = Files.Count(f => f.HasFailed);

        if (Files.Count == 0)
        {
            Summary = "Aucun fichier";
            OverallProgress = 0d;
            return;
        }

        var parts = new List<string> { $"{Files.Count} fichier{Plural(Files.Count)}" };

        if (RunningCount > 0)
        {
            parts.Add($"{RunningCount} en cours");
        }

        if (SucceededCount > 0)
        {
            parts.Add($"{SucceededCount} termine{Plural(SucceededCount)}");
        }

        if (FailedCount > 0)
        {
            parts.Add($"{FailedCount} en echec");
        }

        Summary = string.Join("  ·  ", parts);

        var tracked = Files.Where(f => !f.IsPending).ToList();
        OverallProgress = tracked.Count == 0 ? 0d : tracked.Average(f => f.IsFinished ? 100d : f.Progress);
    }

    private static string Plural(int count) => count > 1 ? "s" : string.Empty;

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) && parsed > 0d
            ? parsed
            : null;

    /// <summary>Accepte « 90 », « 1:30 » ou « 00:01:30.5 ».</summary>
    private static TimeSpan? ParseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(value, CultureInfo.CurrentCulture, out var parsed) ? parsed : null;
    }
}
