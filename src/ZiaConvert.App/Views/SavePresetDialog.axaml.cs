using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ZiaConvert.App.Views;

/// <summary>
/// Demande un nom pour le preglage en cours d'enregistrement.
/// </summary>
/// <remarks>
/// Une simple boite de saisie, pas un formulaire : les reglages eux-memes sont deja
/// visibles dans le panneau derriere cette fenetre, il n'y a qu'un nom a choisir.
/// </remarks>
public partial class SavePresetDialog : Window
{
    public SavePresetDialog()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
        NameBox.KeyDown += OnNameBoxKeyDown;
    }

    /// <summary>Nom choisi, deja nettoye des espaces superflus. <c>null</c> si annule.</summary>
    public static async Task<string?> AskAsync(Window owner)
    {
        var dialog = new SavePresetDialog();

        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Save();
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        var name = NameBox.Text?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ErrorText.Text = "Le nom ne peut pas etre vide.";
            ErrorText.IsVisible = true;
            return;
        }

        Close(name);
    }
}
