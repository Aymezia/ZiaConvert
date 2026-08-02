using System.Diagnostics;

namespace ZiaConvert.App;

/// <summary>
/// Telecharge l'installateur d'une nouvelle version et le lance en silencieux.
/// </summary>
/// <remarks>
/// Ne remplace jamais les fichiers de l'application en cours d'execution soi-meme :
/// c'est l'installateur Inno Setup lui-meme qui s'en charge, avec <c>/CLOSEAPPLICATIONS</c>
/// et <c>/RESTARTAPPLICATIONS</c> (voir installer/ZiaConvert.iss) comme filet de securite
/// si l'application n'a pas eu le temps de se fermer d'elle-meme au prealable.
/// </remarks>
internal static class AppUpdater
{
    public static async Task DownloadAndRunAsync(
        string installerUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZiaConvert-Updater");

        using var response = await http
            .GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var installerPath = Path.Combine(Path.GetTempPath(), $"ZiaConvert-Setup-{Guid.NewGuid():N}.exe");

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(installerPath))
        {
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readTotal += read;

                if (totalBytes is > 0)
                {
                    progress?.Report(readTotal * 100d / totalBytes.Value);
                }
            }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true,
        });
    }
}
