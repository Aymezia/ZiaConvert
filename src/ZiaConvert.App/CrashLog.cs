using System.Globalization;
using System.Text;
using Avalonia.Threading;

namespace ZiaConvert.App;

/// <summary>
/// Consigne les exceptions non gerees dans un fichier.
/// </summary>
/// <remarks>
/// Une application graphique qui se ferme sans un mot est indebogable une fois chez
/// l'utilisateur : il ne reste ni console ni trace. Ce journal est le seul indice
/// exploitable quand quelqu'un signale que « ca a disparu tout seul ».
/// </remarks>
internal static class CrashLog
{
    private static readonly Lock Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZiaConvert",
        "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Tache non observee", e.Exception);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("Fil de l'interface", e.Exception);

            // On ne laisse pas l'exception fermer l'application : une conversion en echec
            // ne doit pas emporter la fenetre et la file entiere avec elle.
            e.Handled = true;
        };
    }

    public static void Write(string origin, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"===== {DateTimeOffset.Now:u} — {origin}")
                .AppendLine(exception.ToString())
                .AppendLine();

            lock (Gate)
            {
                File.AppendAllText(Path, entry.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Journaliser l'echec du journal n'avancerait a rien.
        }
    }
}
