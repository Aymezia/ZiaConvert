using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Tools;
using ZiaConvert.Engines;

namespace ZiaConvert.Engines.Tests.FFmpeg;

/// <summary>
/// Fabrique une fois pour toutes les medias necessaires aux tests d'integration.
/// </summary>
/// <remarks>
/// Les fichiers sont generes par ffmpeg lui-meme plutot que versionnes : le depot reste
/// leger, et les medias sont reproductibles a l'identique sur n'importe quelle machine.
/// </remarks>
public sealed class FFmpegMediaFixture : IAsyncLifetime
{
    private string _root = string.Empty;

    public ConversionServices Services { get; } = ConversionServices.Create();

    /// <summary>Clip court en h264 + aac : le cas courant, compatible mp4 comme mkv.</summary>
    public string ShortVideo => Path.Combine(_root, "court.mp4");

    /// <summary>
    /// Clip volontairement long, destine aux tests d'annulation : il faut que la conversion
    /// dure assez pour etre interrompue en plein milieu.
    /// </summary>
    public string LongVideo => Path.Combine(_root, "long.mp4");

    /// <summary>Fichier au contenu invalide, pour eprouver les messages d'erreur.</summary>
    public string CorruptFile => Path.Combine(_root, "corrompu.mp4");

    public string WorkDirectory => Path.Combine(_root, "sortie");

    public string OutputPath(string fileName) => Path.Combine(WorkDirectory, fileName);

    public async Task InitializeAsync()
    {
        _root = Directory.CreateTempSubdirectory("ziaconvert-tests-").FullName;
        Directory.CreateDirectory(WorkDirectory);

        var ffmpeg = new ToolLocator().Locate("ffmpeg");
        Assert.NotNull(ffmpeg);

        await GenerateAsync(ffmpeg, ShortVideo, durationSeconds: 3, size: "640x480");
        await GenerateAsync(ffmpeg, LongVideo, durationSeconds: 150, size: "854x480");

        await File.WriteAllTextAsync(CorruptFile, "ceci n'est pas une video");
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Un fichier encore verrouille ne doit pas faire echouer la serie de tests.
        }

        return Task.CompletedTask;
    }

    private static async Task GenerateAsync(string ffmpeg, string path, int durationSeconds, string size)
    {
        var runner = new ProcessRunner();
        var duration = durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var arguments = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")
            .Add("-y")
            .Add("-f", "lavfi")
            .Add("-i", $"testsrc=size={size}:rate=30:duration={duration}")
            .Add("-f", "lavfi")
            .Add("-i", $"sine=frequency=440:duration={duration}")
            .Add("-c:v", "libx264")
            .Add("-preset", "ultrafast")
            .Add("-pix_fmt", "yuv420p")
            .Add("-c:a", "aac")
            .Add(path)
            .Build();

        var result = await runner.RunAsync(new ProcessRequest { FileName = ffmpeg, Arguments = arguments });

        Assert.True(result.Success, result.StandardErrorText);
    }
}

[CollectionDefinition("ffmpeg")]
public sealed class FFmpegCollection : ICollectionFixture<FFmpegMediaFixture>;
