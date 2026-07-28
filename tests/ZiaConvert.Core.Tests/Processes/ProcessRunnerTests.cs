using System.Diagnostics;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Core.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task StreamAsync_emet_les_lignes_de_la_sortie_standard()
    {
        var lines = await CollectAsync(TestCommands.Echo("bonjour"));

        var line = Assert.Single(lines);
        Assert.Equal(ProcessStreamKind.StandardOutput, line.Stream);
        Assert.Equal("bonjour", line.Text.Trim());
    }

    [Fact]
    public async Task StreamAsync_distingue_la_sortie_d_erreur()
    {
        var lines = await CollectAsync(TestCommands.EchoError("probleme"));

        var line = Assert.Single(lines);
        Assert.True(line.IsError);
        Assert.Equal("probleme", line.Text.Trim());
    }

    [Fact]
    public async Task StreamAsync_leve_avec_le_code_de_sortie_en_cas_d_echec()
    {
        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => CollectAsync(TestCommands.Exit(3)));

        Assert.Equal(3, exception.ExitCode);
    }

    [Fact]
    public async Task StreamAsync_conserve_la_sortie_d_erreur_dans_l_exception()
    {
        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => CollectAsync(TestCommands.EchoBothThenExit("ok", "la-cause-reelle", 1)));

        Assert.Contains(exception.ErrorTail, l => l.Contains("la-cause-reelle", StringComparison.Ordinal));
        Assert.Contains("la-cause-reelle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_limite_la_sortie_conservee_a_ErrorTailLines()
    {
        var request = TestCommands.EchoBothThenExit("ok", "erreur", 1) with { ErrorTailLines = 0 };

        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(() => CollectAsync(request));

        Assert.Empty(exception.ErrorTail);
    }

    [Fact]
    public async Task StreamAsync_signale_un_executable_introuvable_comme_un_echec_normal()
    {
        // Les moteurs ne doivent avoir qu'un seul type d'exception a traiter : un binaire
        // absent ne doit pas remonter sous la forme d'une Win32Exception brute.
        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => CollectAsync(TestCommands.Missing()));

        Assert.Equal(-1, exception.ExitCode);
    }

    [Fact]
    public async Task StreamAsync_ne_perd_aucune_ligne_avant_la_fin_du_processus()
    {
        // Verifie que le canal n'est ferme qu'apres epuisement des deux flux : le fermer
        // sur le premier ferait disparaitre silencieusement une partie de la sortie.
        const int expected = 300;

        var lines = await CollectAsync(TestCommands.EchoManyLines(expected));

        var emitted = lines.Count(l => l.Text.Trim().StartsWith("line", StringComparison.Ordinal));
        Assert.Equal(expected, emitted);
    }

    [Fact]
    public async Task RunAsync_rend_le_code_de_sortie_sans_lever()
    {
        var result = await _runner.RunAsync(TestCommands.EchoBothThenExit("sortie", "erreur", 2));

        Assert.False(result.Success);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.StandardOutput, l => l.Contains("sortie", StringComparison.Ordinal));
        Assert.Contains(result.StandardError, l => l.Contains("erreur", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_reussit_sur_un_code_zero()
    {
        var result = await _runner.RunAsync(TestCommands.Echo("ok"));

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task StreamAsync_arrete_le_processus_a_l_annulation()
    {
        // La commande n'ecrit rien sur ses flux : l'annulation ne peut pas etre declenchee
        // depuis la boucle, elle est donc programmee dans le temps.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var request = TestCommands.LongRunning() with
        {
            GracefulStopInput = "q",
            GracefulStopTimeout = TimeSpan.FromMilliseconds(300),
        };

        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _runner.StreamAsync(request, cts.Token))
            {
                // Aucune ligne attendue.
            }
        });

        stopwatch.Stop();

        // Le processus dort 30 s : s'il fallait attendre sa fin naturelle, on serait tres au-dela.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"L'annulation a pris {stopwatch.Elapsed.TotalSeconds:0.0} s, le processus n'a pas ete termine.");
    }

    [Fact]
    public async Task StreamAsync_leve_OperationCanceled_meme_si_le_processus_reussit()
    {
        // Course a l'annulation : le processus peut se terminer normalement pendant qu'on
        // demande l'arret. L'appelant doit malgre tout voir une annulation, jamais un succes.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(TestCommands.Echo("ok"), cts.Token));
    }

    private async Task<List<ProcessOutputLine>> CollectAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<ProcessOutputLine>();

        await foreach (var line in _runner.StreamAsync(request, cancellationToken))
        {
            lines.Add(line);
        }

        return lines;
    }
}
