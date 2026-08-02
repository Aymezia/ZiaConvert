using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Tools;
using ZiaConvert.Engines;

namespace ZiaConvert.Engines.Tests.Upscale;

/// <summary>
/// Fabrique les images necessaires aux tests d'integration Real-ESRGAN, generees par
/// ImageMagick plutot que versionnees.
/// </summary>
public sealed class RealEsrganMediaFixture : IAsyncLifetime
{
    private string _root = string.Empty;

    public ConversionServices Services { get; } = ConversionServices.Create();

    /// <summary>Petite photo de synthese 120x90, rapide a agrandir.</summary>
    public string SampleImage => Path.Combine(_root, "photo.jpg");

    /// <summary>
    /// Image volontairement grande : le seul moyen de laisser assez de temps pour
    /// annuler une conversion en cours avant qu'elle ne se termine d'elle-meme.
    /// </summary>
    public string LargeImage => Path.Combine(_root, "grande-photo.png");

    public string WorkDirectory => Path.Combine(_root, "sortie");

    public string OutputPath(string fileName) => Path.Combine(WorkDirectory, fileName);

    public async Task InitializeAsync()
    {
        _root = Directory.CreateTempSubdirectory("ziaconvert-upscale-tests-").FullName;
        Directory.CreateDirectory(WorkDirectory);

        var magick = new ToolLocator().Locate("magick");
        Assert.NotNull(magick);

        await GenerateAsync(magick, ["-size", "120x90", "gradient:#3a6ea5-#c0574a"], SampleImage);
        await GenerateAsync(magick, ["-size", "1800x1800", "plasma:fractal"], LargeImage);
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

    private static async Task GenerateAsync(string magick, IReadOnlyList<string> generator, string path)
    {
        var runner = new ProcessRunner();
        var arguments = new ArgumentBuilder().AddRange(generator).Add(path).Build();

        var result = await runner.RunAsync(new ProcessRequest { FileName = magick, Arguments = arguments });

        Assert.True(result.Success, result.StandardErrorText);
    }
}

[CollectionDefinition("realesrgan")]
public sealed class RealEsrganCollection : ICollectionFixture<RealEsrganMediaFixture>;
