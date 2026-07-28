using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Routing;

namespace ZiaConvert.Core.Tests.Routing;

public sealed class FormatRegistryTests
{
    private readonly FormatRegistry _registry = FormatRegistry.Default;

    [Theory]
    [InlineData(".mp4", "mp4")]
    [InlineData("mp4", "mp4")]
    [InlineData(".MP4", "mp4")]
    [InlineData(".jpeg", "jpeg")]
    [InlineData(".jpg", "jpeg")]
    [InlineData(".CR2", "cr2")]
    [InlineData(".docx", "docx")]
    public void FindByExtension_tolere_le_point_et_la_casse(string extension, string expectedId)
    {
        var format = _registry.FindByExtension(extension);

        Assert.NotNull(format);
        Assert.Equal(expectedId, format.Id);
    }

    [Fact]
    public void FindByExtension_rend_null_pour_un_format_inconnu()
    {
        Assert.Null(_registry.FindByExtension(".xyz"));
        Assert.Null(_registry.FindByExtension(""));
    }

    [Fact]
    public void GetByPath_leve_un_message_exploitable_pour_un_format_inconnu()
    {
        var exception = Assert.Throws<UnsupportedConversionException>(
            () => _registry.GetByPath(@"C:\dossier\fichier.xyz"));

        Assert.Contains(".xyz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetByPath_signale_specifiquement_l_absence_d_extension()
    {
        var exception = Assert.Throws<UnsupportedConversionException>(
            () => _registry.GetByPath(@"C:\dossier\fichier"));

        Assert.Contains("extension", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_identifiants_sont_uniques()
    {
        var duplicates = _registry.All
            .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Chaque_format_declare_au_moins_une_extension()
    {
        Assert.All(_registry.All, format => Assert.NotEmpty(format.Extensions));
    }

    [Fact]
    public void Les_extensions_sont_normalisees_avec_un_point_en_minuscules()
    {
        var extensions = _registry.All.SelectMany(f => f.Extensions);

        Assert.All(extensions, extension =>
        {
            Assert.StartsWith(".", extension, StringComparison.Ordinal);
            Assert.Equal(extension.ToLowerInvariant(), extension);
        });
    }

    [Fact]
    public void Un_RAW_ne_peut_jamais_etre_un_format_de_sortie()
    {
        // On developpe un negatif numerique, on n'en fabrique pas.
        var rawFormats = _registry.ByFamily(FormatFamily.RawImage);

        Assert.NotEmpty(rawFormats);
        Assert.All(rawFormats, format => Assert.False(format.CanBeTarget));
    }

    [Fact]
    public void TargetsFor_propose_l_extraction_audio_depuis_une_video()
    {
        var source = _registry.FindById("mp4");
        Assert.NotNull(source);

        var targets = _registry.TargetsFor(source).ToList();

        Assert.Contains(targets, f => f.Id == "mkv");
        Assert.Contains(targets, f => f.Id == "mp3");
        Assert.Contains(targets, f => f.Id == "gif");
    }

    [Fact]
    public void TargetsFor_ne_propose_jamais_le_format_source()
    {
        foreach (var source in _registry.All)
        {
            Assert.DoesNotContain(_registry.TargetsFor(source), f => f.Id == source.Id);
        }
    }

    [Fact]
    public void TargetsFor_developpe_un_RAW_vers_une_image_et_rien_d_autre()
    {
        var source = _registry.FindById("cr2");
        Assert.NotNull(source);

        var targets = _registry.TargetsFor(source).ToList();

        Assert.Contains(targets, f => f.Id == "jpeg");
        Assert.All(targets, f => Assert.Equal(FormatFamily.Image, f.Family));
    }

    [Fact]
    public void TargetsFor_ne_melange_pas_documents_et_medias()
    {
        var source = _registry.FindById("docx");
        Assert.NotNull(source);

        var targets = _registry.TargetsFor(source).ToList();

        Assert.Contains(targets, f => f.Id == "pdf");
        Assert.All(targets, f => Assert.Equal(FormatFamily.Document, f.Family));
    }

    [Fact]
    public void TargetsFor_ne_propose_aucune_sortie_video_depuis_un_fichier_audio()
    {
        var source = _registry.FindById("mp3");
        Assert.NotNull(source);

        Assert.All(_registry.TargetsFor(source), f => Assert.Equal(FormatFamily.Audio, f.Family));
    }
}
