using ZiaConvert.Core.Jobs;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Routing;

namespace ZiaConvert.Core.Tests.Jobs;

public sealed class JobQueueTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ziaconvert-queue-").FullName;
    private readonly FakeEngine _engine = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Sans consequence pour la serie de tests.
        }
    }

    [Fact]
    public async Task Traite_les_conversions_deposees()
    {
        await using var queue = CreateQueue();

        var job = queue.Enqueue(Request("a.mp4", "a.mkv"));
        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(JobState.Completed, job.State);
        Assert.NotNull(job.Result);
        Assert.True(job.Result.Success);
    }

    [Fact]
    public async Task Traite_un_lot_complet()
    {
        await using var queue = CreateQueue();

        var jobs = Enumerable.Range(0, 12)
            .Select(i => queue.Enqueue(Request($"lot{i}.mp4", $"lot{i}.mkv")))
            .ToList();

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.All(jobs, j => Assert.Equal(JobState.Completed, j.State));
        Assert.Equal(12, _engine.StartedCount);
    }

    [Fact]
    public async Task Ne_depasse_jamais_la_limite_de_parallelisme_video()
    {
        // La raison d'etre de la file : douze encodages lances d'un coup mettraient la
        // machine a genoux sans aller plus vite.
        var policy = new ConcurrencyPolicy { Video = 2 };
        await using var queue = CreateQueue(policy);

        for (var i = 0; i < 12; i++)
        {
            queue.Enqueue(Request($"video{i}.mp4", $"video{i}.mkv"));
        }

        await queue.WaitForIdleAsync(TestTimeout());

        // Exactement 2, pas « au plus 2 » : la borne serait aussi respectee par une file
        // devenue sequentielle par accident, ce qui gacherait la moitie de la machine.
        Assert.Equal(2, _engine.PeakConcurrency(FormatFamily.Video));
    }

    [Fact]
    public async Task Sequentialise_les_documents()
    {
        // LibreOffice refuse deux instances partageant le meme profil : la limite a 1
        // n'est pas un reglage de confort, c'est une contrainte du moteur.
        await using var queue = CreateQueue(new ConcurrencyPolicy { Document = 1 });

        for (var i = 0; i < 6; i++)
        {
            queue.Enqueue(Request($"doc{i}.docx", $"doc{i}.pdf"));
        }

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(1, _engine.PeakConcurrency(FormatFamily.Document));
    }

    [Fact]
    public async Task Laisse_les_familles_progresser_en_parallele()
    {
        // Une conversion d'image ne doit pas attendre la fin d'un encodage video : les
        // limites sont independantes d'une famille a l'autre.
        var policy = new ConcurrencyPolicy { Video = 1, Image = 2, Audio = 1, Document = 1 };
        await using var queue = CreateQueue(policy);

        var video = queue.Enqueue(Request("film.mp4", "film.mkv"));
        var image = queue.Enqueue(Request("photo.png", "photo.jpg"));

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(JobState.Completed, video.State);
        Assert.Equal(JobState.Completed, image.State);
    }

    [Fact]
    public async Task Une_conversion_annulee_avant_son_tour_ne_demarre_jamais()
    {
        _engine.Duration = TimeSpan.FromMilliseconds(400);
        await using var queue = CreateQueue(new ConcurrencyPolicy { Video = 1 });

        queue.Enqueue(Request("premier.mp4", "premier.mkv"));
        var second = queue.Enqueue(Request("second.mp4", "second.mkv"));

        second.Cancel();
        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(JobState.Cancelled, second.State);
        Assert.False(File.Exists(Path.Combine(_directory, "second.mkv")));
    }

    [Fact]
    public async Task Une_conversion_en_cours_peut_etre_annulee()
    {
        _engine.Duration = TimeSpan.FromSeconds(5);
        await using var queue = CreateQueue();

        var job = queue.Enqueue(Request("long.mp4", "long.mkv"));

        await WaitUntilAsync(() => job.State == JobState.Running);
        job.Cancel();

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(JobState.Cancelled, job.State);
    }

    [Fact]
    public async Task Un_echec_n_interrompt_pas_le_reste_du_lot()
    {
        _engine.FailOnPathContaining = "casse";
        await using var queue = CreateQueue();

        var failing = queue.Enqueue(Request("casse.mp4", "casse.mkv"));
        var others = Enumerable.Range(0, 4)
            .Select(i => queue.Enqueue(Request($"sain{i}.mp4", $"sain{i}.mkv")))
            .ToList();

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.Equal(JobState.Failed, failing.State);
        Assert.NotNull(failing.Result?.ErrorMessage);
        Assert.All(others, j => Assert.Equal(JobState.Completed, j.State));
    }

    [Fact]
    public async Task Signale_chaque_evolution()
    {
        await using var queue = CreateQueue();

        var states = new List<JobState>();
        queue.JobChanged += (_, job) =>
        {
            lock (states)
            {
                states.Add(job.State);
            }
        };

        queue.Enqueue(Request("suivi.mp4", "suivi.mkv"));
        await queue.WaitForIdleAsync(TestTimeout());

        lock (states)
        {
            Assert.Contains(JobState.Running, states);
            Assert.Contains(JobState.Completed, states);
        }
    }

    [Fact]
    public async Task CancelAll_arrete_tout_le_lot()
    {
        _engine.Duration = TimeSpan.FromSeconds(5);
        await using var queue = CreateQueue(new ConcurrencyPolicy { Video = 1 });

        var jobs = Enumerable.Range(0, 5)
            .Select(i => queue.Enqueue(Request($"tout{i}.mp4", $"tout{i}.mkv")))
            .ToList();

        await WaitUntilAsync(() => jobs.Any(j => j.State == JobState.Running));
        queue.CancelAll();

        await queue.WaitForIdleAsync(TestTimeout());

        Assert.All(jobs, j => Assert.Equal(JobState.Cancelled, j.State));
    }

    private JobQueue CreateQueue(ConcurrencyPolicy? policy = null)
    {
        var router = new ConversionRouter([_engine]);
        var executor = new ConversionExecutor(router);

        return new JobQueue(executor, policy ?? new ConcurrencyPolicy());
    }

    private ConversionRequest Request(string input, string output)
    {
        var registry = FormatRegistry.Default;

        return new ConversionRequest
        {
            InputPath = Path.Combine(_directory, input),
            OutputPath = Path.Combine(_directory, output),
            SourceFormat = registry.GetByPath(input),
            TargetFormat = registry.GetByPath(output),
        };
    }

    private static CancellationToken TestTimeout() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
