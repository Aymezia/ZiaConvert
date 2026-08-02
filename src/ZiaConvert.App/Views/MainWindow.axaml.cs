using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ZiaConvert.App.ViewModels;

namespace ZiaConvert.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (ViewModel is not { } viewModel || e.DataTransfer.TryGetFiles() is not { } items)
        {
            return;
        }

        var paths = items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .SelectMany(path => Expand(path!));

        viewModel.AddFiles(paths);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choisir des fichiers a convertir",
                AllowMultiple = true,
            });

            ViewModel?.AddFiles(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!));
        }
#pragma warning disable CA1031 // Un selecteur de fichiers en echec ne doit pas fermer l'application.
        catch (Exception)
        {
            // Rien de plus a faire : l'utilisateur peut toujours glisser ses fichiers.
        }
#pragma warning restore CA1031
    }

    private async void OnAddSubtitleClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choisir des fichiers de sous-titres",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Sous-titres")
                    {
                        Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt"],
                    },
                ],
            });

            viewModel.AddSubtitleFiles(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!));
        }
#pragma warning disable CA1031 // Un selecteur de fichiers en echec ne doit pas fermer l'application.
        catch (Exception)
        {
            // Rien de plus a faire : la section reste vide, l'utilisateur peut reessayer.
        }
#pragma warning restore CA1031
    }

    private async void OnSavePresetClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var name = await SavePresetDialog.AskAsync(this);

        if (!string.IsNullOrWhiteSpace(name))
        {
            viewModel.SaveCurrentAsPreset(name.Trim());
        }
    }

    /// <summary>
    /// Deplie les dossiers deposes. Glisser un dossier de rushes est un geste naturel ;
    /// ne rien faire dans ce cas passerait pour un bogue.
    /// </summary>
    private static IEnumerable<string> Expand(string path)
    {
        if (!Directory.Exists(path))
        {
            return [path];
        }

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
