using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZiaConvert.Core.Jobs;
using ZiaConvert.Core.Model;

namespace ZiaConvert.App.ViewModels;

/// <summary>
/// Une ligne de la liste : un fichier depose, puis la conversion qui lui correspond.
/// </summary>
public sealed partial class FileEntryViewModel : ObservableObject
{
    private ConversionJob? _job;

    [ObservableProperty]
    private string _status = "En attente";

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate;

    /// <summary>Vrai tant que le job n'a pas commence a s'executer. Pilote le badge « en attente ».</summary>
    [ObservableProperty]
    private bool _isQueued = true;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private bool _hasFailed;

    [ObservableProperty]
    private bool _isCancelled;

    /// <summary>Vrai pour un fichier ecarte du lot (deja au format cible, conversion sans objet).</summary>
    [ObservableProperty]
    private bool _isSkipped;

    /// <summary>
    /// Mot court affiche dans le badge de statut. Separe de <see cref="Status" />, qui
    /// porte le detail complet (pourcentage, vitesse, ETA en cours de route) : un badge
    /// de quelques caracteres ne peut pas accueillir cette phrase sans deborder.
    /// </summary>
    [ObservableProperty]
    private string _badgeWord = "ATTENTE";

    /// <summary>
    /// Distinct de <see cref="IsFinished" />, qui couvre aussi l'echec et l'annulation :
    /// seule une reussite doit s'afficher en vert.
    /// </summary>
    [ObservableProperty]
    private bool _isSucceeded;

    [ObservableProperty]
    private bool _canCancel = true;

    [ObservableProperty]
    private string _targetLabel = "?";

    public FileEntryViewModel(string path, MediaFormat sourceFormat)
    {
        Path = path;
        SourceFormat = sourceFormat;
        FileName = System.IO.Path.GetFileName(path);

        var info = new FileInfo(path);
        SizeText = info.Exists ? FormatSize(info.Length) : string.Empty;
    }

    public string Path { get; }

    public string FileName { get; }

    public string SizeText { get; }

    public MediaFormat SourceFormat { get; }

    public string SourceLabel => SourceFormat.Id;

    /// <summary>Vrai tant que le fichier n'a pas ete confie a la file d'attente.</summary>
    public bool IsPending => _job is null;

    public Guid? JobId => _job?.Id;

    public void Attach(ConversionJob job)
    {
        _job = job;
        TargetLabel = job.Request.TargetFormat.Id;
        Refresh();
    }

    /// <summary>
    /// Recopie l'etat du job dans les proprietes liees.
    /// </summary>
    /// <remarks>
    /// A n'appeler que depuis le fil de l'interface : les evenements de la file sont
    /// leves par les workers, il revient a l'appelant de faire le saut de contexte.
    /// </remarks>
    public void Refresh()
    {
        if (_job is null)
        {
            return;
        }

        var progress = _job.Progress;

        switch (_job.State)
        {
            case JobState.Queued:
                Status = "En attente";
                IsIndeterminate = false;
                Progress = 0d;
                break;

            case JobState.Running:
                IsQueued = false;
                IsRunning = true;
                IsIndeterminate = progress?.Percent is null;
                Progress = progress?.Percent ?? 0d;
                Status = DescribeRunning(progress);
                BadgeWord = "CONVERSION";
                break;

            case JobState.Completed:
                IsQueued = false;
                IsRunning = false;
                IsFinished = true;
                IsSucceeded = true;
                CanCancel = false;
                IsIndeterminate = false;
                Progress = 100d;
                Status = $"Termine en {Seconds(_job.Result?.Duration)}";
                Detail = _job.Result?.Detail;
                BadgeWord = "TERMINE";
                break;

            case JobState.Failed:
                IsQueued = false;
                IsRunning = false;
                IsFinished = true;
                HasFailed = true;
                CanCancel = false;
                IsIndeterminate = false;
                Progress = 0d;
                Status = "Echec";
                Detail = _job.Result?.ErrorMessage;
                BadgeWord = "ECHEC";
                break;

            case JobState.Cancelled:
                IsQueued = false;
                IsRunning = false;
                IsFinished = true;
                IsCancelled = true;
                CanCancel = false;
                IsIndeterminate = false;
                Progress = 0d;
                Status = "Annule";
                Detail = null;
                BadgeWord = "ANNULE";
                break;

            default:
                break;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _job?.Cancel();
        CanCancel = false;
    }

    /// <summary>
    /// Marque la ligne comme ecartee du lot, sans passer par la file de conversion.
    /// </summary>
    /// <remarks>
    /// Un fichier deja au format cible, ou dont la conversion demandee n'a pas de sens,
    /// n'est pas un echec : il n'y a simplement rien a faire pour lui. Le distinguer d'un
    /// <see cref="HasFailed" /> evite de laisser croire a un probleme reel.
    /// </remarks>
    public void MarkSkipped(string reason)
    {
        Status = reason;
        IsQueued = false;
        IsFinished = true;
        IsSkipped = true;
        CanCancel = false;
        BadgeWord = "IGNORE";
    }

    private static string DescribeRunning(ConversionProgress? progress)
    {
        if (progress is null)
        {
            return "Demarrage...";
        }

        if (progress.Stage == ConversionStage.Analyzing)
        {
            return "Analyse...";
        }

        if (progress.Percent is not { } percent)
        {
            return progress.Message ?? "Conversion...";
        }

        var text = percent.ToString("0", CultureInfo.CurrentCulture) + " %";

        if (progress.Speed is { } speed)
        {
            text += "   " + speed.ToString("0.0", CultureInfo.CurrentCulture) + "x";
        }

        if (progress.Eta is { } eta && eta > TimeSpan.Zero)
        {
            text += "   reste " + eta.ToString(eta.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        }

        return text;
    }

    private static string Seconds(TimeSpan? duration) =>
        duration is { } value
            ? value.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s"
            : "-";

    private static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
