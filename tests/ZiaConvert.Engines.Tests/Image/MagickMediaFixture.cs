using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Tools;
using ZiaConvert.Engines;

namespace ZiaConvert.Engines.Tests.Image;

/// <summary>
/// Fabrique les images necessaires aux tests d'integration, generees par ImageMagick
/// lui-meme plutot que versionnees.
/// </summary>
/// <remarks>
/// Un vrai fichier RAW (CR2/NEF/ARW...) ne peut pas etre synthetise de la meme facon :
/// c'est un enregistrement de la mosaique du capteur, propre a chaque appareil. Cette
/// fixture cherche un fichier RAW deja present sur la machine dans quelques emplacements
/// courants ; s'il n'y en a aucun, <see cref="RawSample" /> vaut <c>null</c> et les tests
/// qui en ont besoin s'excluent d'eux-memes plutot que d'echouer.
/// </remarks>
public sealed class MagickMediaFixture : IAsyncLifetime
{
    private static readonly string[] RawExtensions = [".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2"];

    private string _root = string.Empty;

    public ConversionServices Services { get; } = ConversionServices.Create();

    /// <summary>Photo de synthese 400x300, opaque.</summary>
    public string SamplePng => Path.Combine(_root, "photo.png");

    /// <summary>Meme photo avec un canal alpha, pour verifier la conversion vers un format sans transparence.</summary>
    public string SamplePngWithAlpha => Path.Combine(_root, "photo-alpha.png");

    /// <summary>Fichier au contenu invalide, pour eprouver les messages d'erreur.</summary>
    public string CorruptFile => Path.Combine(_root, "corrompu.jpg");

    /// <summary>Chemin d'un vrai fichier RAW trouve sur la machine, ou <c>null</c> si aucun n'est disponible.</summary>
    public string? RawSample { get; private set; }

    public string WorkDirectory => Path.Combine(_root, "sortie");

    public string OutputPath(string fileName) => Path.Combine(WorkDirectory, fileName);

    public async Task InitializeAsync()
    {
        _root = Directory.CreateTempSubdirectory("ziaconvert-magick-tests-").FullName;
        Directory.CreateDirectory(WorkDirectory);

        var magick = new ToolLocator().Locate("magick");
        Assert.NotNull(magick);

        await GenerateAsync(magick, ["-size", "400x300", "gradient:#3a6ea5-#c0574a"], SamplePng);
        await GenerateAsync(
            magick,
            ["-size", "400x300", "gradient:#3a6ea5-#c0574a", "-alpha", "set", "-channel", "A", "-evaluate", "set", "50%"],
            SamplePngWithAlpha);

        await File.WriteAllTextAsync(CorruptFile, "ceci n'est pas une image");

        RawSample = FindRealRawFile();
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

    private static string? FindRealRawFile()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        ];

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                var found = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => RawExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

                if (found is not null)
                {
                    return found;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier inaccessible : on continue sur les autres emplacements.
            }
        }

        return null;
    }

    private static async Task GenerateAsync(string magick, IReadOnlyList<string> generator, string path)
    {
        var runner = new ProcessRunner();
        var arguments = new ArgumentBuilder().AddRange(generator).Add(path).Build();

        var result = await runner.RunAsync(new ProcessRequest { FileName = magick, Arguments = arguments });

        Assert.True(result.Success, result.StandardErrorText);
    }
}

[CollectionDefinition("magick")]
public sealed class MagickCollection : ICollectionFixture<MagickMediaFixture>;
