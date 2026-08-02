using System.Net;
using ZiaConvert.Core.Updates;

namespace ZiaConvert.Core.Tests.Updates;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task Signale_une_version_plus_recente()
    {
        var checker = Checker(HttpStatusCode.OK, Release("v0.3.0"));

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.NotNull(update);
        Assert.Equal("0.3.0", update!.Version);
        Assert.Equal("https://github.com/Aymezia/ZiaConvert/releases/download/v0.3.0/ZiaConvert-Setup-0.3.0.exe", update.InstallerUrl);
    }

    [Fact]
    public async Task Ne_signale_rien_quand_la_version_est_identique()
    {
        var checker = Checker(HttpStatusCode.OK, Release("v0.2.0"));

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task Ne_signale_rien_quand_la_release_est_plus_ancienne()
    {
        var checker = Checker(HttpStatusCode.OK, Release("v0.1.0"));

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task Compare_correctement_une_version_a_4_composants_a_un_tag_a_3()
    {
        // AssemblyVersion rapporte 4 composants (0.2.0.0) ; le tag GitHub n'en a que 3
        // (0.2.0). Une comparaison naive de System.Version les jugerait a tort inegales
        // (la revision -1 non renseignee du tag compte comme inferieure a 0).
        var checker = Checker(HttpStatusCode.OK, Release("v0.2.0"));

        var update = await checker.CheckAsync(new Version(0, 2, 0, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task Ne_signale_rien_sans_installateur_exe_dans_les_assets()
    {
        var checker = Checker(HttpStatusCode.OK, """
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/Aymezia/ZiaConvert/releases/tag/v0.3.0",
              "assets": [
                { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" }
              ]
            }
            """);

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task Ne_leve_aucune_exception_sur_une_reponse_en_echec()
    {
        var checker = Checker(HttpStatusCode.NotFound, null);

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    [Fact]
    public async Task Ne_leve_aucune_exception_sur_un_corps_illisible()
    {
        var checker = Checker(HttpStatusCode.OK, "ceci n'est pas du json");

        var update = await checker.CheckAsync(new Version(0, 2, 0));

        Assert.Null(update);
    }

    private static UpdateChecker Checker(HttpStatusCode status, string? body) =>
        new(new HttpClient(new FakeHandler(status, body)));

    private static string Release(string tag) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/Aymezia/ZiaConvert/releases/tag/{{tag}}",
          "assets": [
            { "name": "ZiaConvert-Setup-{{tag.TrimStart('v')}}.exe", "browser_download_url": "https://github.com/Aymezia/ZiaConvert/releases/download/{{tag}}/ZiaConvert-Setup-{{tag.TrimStart('v')}}.exe" }
          ]
        }
        """;

    private sealed class FakeHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);

            if (body is not null)
            {
                response.Content = new StringContent(body);
            }

            return Task.FromResult(response);
        }
    }
}
