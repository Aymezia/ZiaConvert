using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZiaConvert.App.ViewModels;
using ZiaConvert.App.Views;

namespace ZiaConvert.App;

/// <summary>
/// Application Avalonia.
/// </summary>
/// <remarks>
/// Nommee <c>ZiaConvertApp</c> et non <c>App</c> : la classe cohabiterait sinon avec
/// l'espace de noms <c>ZiaConvert.App</c>, ce que le compilateur accepte mais qui rend
/// chaque reference ambigue a la lecture.
/// </remarks>
public sealed partial class ZiaConvertApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        CrashLog.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();

            // Fichiers passes en argument : c'est ce que fournit « Ouvrir avec » de
            // l'explorateur, et un glisser-deposer sur l'icone de l'application.
            if (desktop.Args is { Length: > 0 } arguments)
            {
                viewModel.AddFiles(arguments);
            }

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // La file lance des processus externes : sans arret explicite, des ffmpeg
            // orphelins survivraient a la fermeture de la fenetre.
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
