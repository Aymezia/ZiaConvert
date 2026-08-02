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

    /// <summary>
    /// MPEG-2 + AC3 dans un conteneur VOB : reproduit le profil d'un rip DVD, timestamps
    /// discontinus compris (verifie : sans <c>-fflags +genpts</c>, le remux vers matroska
    /// echoue avec « Can't write packet with unknown timestamp »).
    /// </summary>
    public string DvdRip => Path.Combine(_root, "rip_dvd.vob");

    /// <summary>
    /// Deux pistes audio distinctes (440 Hz/eng, 880 Hz/fra), pour verifier qu'une
    /// selection de piste retient bien celle demandee et pas l'autre.
    /// </summary>
    public string MultiTrackVideo => Path.Combine(_root, "multi.mkv");

    /// <summary>Fichier .srt externe, a integrer a une sortie mkv sans reencoder le reste.</summary>
    public string SubtitleFile => Path.Combine(_root, "vostfr.srt");

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
        await GenerateDvdRipAsync(ffmpeg);
        await GenerateMultiTrackAsync(ffmpeg);

        await File.WriteAllTextAsync(CorruptFile, "ceci n'est pas une video");

        await File.WriteAllTextAsync(SubtitleFile, """
            1
            00:00:00,000 --> 00:00:01,500
            Bonjour le monde

            2
            00:00:01,500 --> 00:00:03,000
            Ceci est un test

            """);
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

    private async Task GenerateDvdRipAsync(string ffmpeg)
    {
        var runner = new ProcessRunner();

        var arguments = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")
            .Add("-y")
            .Add("-f", "lavfi")
            .Add("-i", "testsrc=size=720x480:rate=25:duration=3")
            .Add("-f", "lavfi")
            .Add("-i", "sine=frequency=440:duration=3")
            .Add("-c:v", "mpeg2video")
            .Add("-c:a", "ac3")
            .Add("-b:a", "192k")
            .Add(DvdRip)
            .Build();

        var result = await runner.RunAsync(new ProcessRequest { FileName = ffmpeg, Arguments = arguments });

        Assert.True(result.Success, result.StandardErrorText);
    }

    private async Task GenerateMultiTrackAsync(string ffmpeg)
    {
        var runner = new ProcessRunner();

        var arguments = new ArgumentBuilder()
            .Add("-hide_banner")
            .Add("-loglevel", "error")
            .Add("-y")
            .Add("-f", "lavfi")
            .Add("-i", "testsrc=size=320x240:rate=25:duration=3")
            .Add("-f", "lavfi")
            .Add("-i", "sine=frequency=440:duration=3")
            .Add("-f", "lavfi")
            .Add("-i", "sine=frequency=880:duration=3")
            .Add("-map", "0:v").Add("-map", "1:a").Add("-map", "2:a")
            .Add("-metadata:s:a:0", "language=eng")
            .Add("-metadata:s:a:1", "language=fra")
            .Add("-c:v", "libx264")
            .Add("-preset", "ultrafast")
            .Add("-pix_fmt", "yuv420p")
            .Add("-c:a", "aac")
            .Add(MultiTrackVideo)
            .Build();

        var result = await runner.RunAsync(new ProcessRequest { FileName = ffmpeg, Arguments = arguments });

        Assert.True(result.Success, result.StandardErrorText);
    }
}

[CollectionDefinition("ffmpeg")]
public sealed class FFmpegCollection : ICollectionFixture<FFmpegMediaFixture>;
