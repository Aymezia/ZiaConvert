using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ZiaConvert.App.ViewModels;

/// <summary>Une piste de sous-titres externe ajoutee au panneau, avant conversion.</summary>
public sealed partial class SubtitleEntryViewModel : ObservableObject
{
    private readonly Action<SubtitleEntryViewModel> _onRemove;

    public SubtitleEntryViewModel(string filePath, Action<SubtitleEntryViewModel> onRemove)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        _onRemove = onRemove;
    }

    public string FilePath { get; }

    public string FileName { get; }

    [ObservableProperty]
    private string _language = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
