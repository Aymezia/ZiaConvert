using Avalonia;

namespace ZiaConvert.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Un echec au demarrage se produit avant que le moindre gestionnaire ne soit
            // installe : sans ce filet, la fenetre ne s'ouvre simplement jamais.
            CrashLog.Write("Demarrage", ex);
            throw;
        }
    }

    /// <summary>Utilise aussi par le concepteur d'interface : ne pas renommer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ZiaConvertApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
