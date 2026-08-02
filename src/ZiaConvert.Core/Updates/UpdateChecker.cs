using System.Text.Json;

namespace ZiaConvert.Core.Updates;

/// <summary>
/// Interroge la derniere release GitHub du depot et compare sa version a celle en cours
/// d'execution.
/// </summary>
/// <remarks>
/// Ne doit jamais faire echouer l'appelant : absence de reseau, depot inaccessible ou
/// reponse inattendue se traduisent tous par « pas de mise a jour disponible », jamais
/// par une exception qui remonterait jusqu'a l'interface au demarrage.
/// </remarks>
public sealed class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Aymezia/ZiaConvert/releases/latest";

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();

        // L'API GitHub refuse toute requete sans User-Agent (403), contrairement a la
        // plupart des API REST publiques.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZiaConvert-UpdateChecker");
        }
    }

    public async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;

            if (root.GetProperty("tag_name").GetString() is not { } tag ||
                !TryParseVersion(tag, out var latest) ||
                Normalize(latest) <= Normalize(currentVersion))
            {
                return null;
            }

            // « /latest » exclut deja les brouillons et prereleases : la premiere entree
            // exploitable est celle a proposer.
            var installerUrl = root.GetProperty("assets").EnumerateArray()
                .Select(asset => asset.GetProperty("browser_download_url").GetString())
                .FirstOrDefault(url => url is { } u && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (installerUrl is null)
            {
                return null;
            }

            var releaseUrl = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;

            return new UpdateInfo
            {
                Version = FormatVersion(latest),
                InstallerUrl = installerUrl,
                ReleaseUrl = releaseUrl ?? installerUrl,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.TrimStart('v', 'V');
        return Version.TryParse(text, out version!);
    }

    /// <summary>
    /// Ramene a Major.Minor.Build (defaut 0 si absent) : comparer directement un
    /// <see cref="Version" /> a 3 composants (tag "0.2.0") a l'AssemblyVersion a 4
    /// composants (0.2.0.0) donnerait un resultat faux, la revision non renseignee (-1)
    /// comptant comme inferieure a 0.
    /// </summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
