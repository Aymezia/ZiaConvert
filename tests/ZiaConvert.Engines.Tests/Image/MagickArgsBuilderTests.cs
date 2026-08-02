using System.Globalization;
using ZiaConvert.Core.Model;
using ZiaConvert.Core.Options;
using ZiaConvert.Core.Routing;
using ZiaConvert.Engines.Image;

namespace ZiaConvert.Engines.Tests.Image;

public sealed class MagickArgsBuilderTests
{
    private readonly MagickArgsBuilder _builder = new();

    [Fact]
    public void Place_l_entree_en_premier_argument()
    {
        var arguments = _builder.Build(Request("photo.jpg", "photo.png"));

        Assert.Equal("photo.jpg", arguments[0]);
    }

    [Fact]
    public void Impose_toujours_le_format_de_sortie()
    {
        // La sortie s'ecrit dans un « .part », dont l'extension ne dit rien a magick :
        // sans le prefixe « format: » explicite, il ne saurait pas quoi produire.
        var request = Request("photo.jpg", "photo.png");

        var arguments = _builder.Build(request);

        Assert.Equal($"png:{request.WorkingPath}", arguments[^1]);
    }

    [Fact]
    public void Traduit_jpeg_vers_le_coder_jpg()
    {
        var request = Request("photo.png", "photo.jpg");

        var arguments = _builder.Build(request);

        Assert.Equal($"jpg:{request.WorkingPath}", arguments[^1]);
    }

    [Fact]
    public void Applique_auto_orient_par_defaut()
    {
        var arguments = _builder.Build(Request("photo.jpg", "photo.png"));

        Assert.Contains("-auto-orient", arguments);
    }

    [Fact]
    public void N_applique_pas_auto_orient_si_desactive()
    {
        var request = Request("photo.jpg", "photo.png", new ImageOptions { AutoOrient = false });

        var arguments = _builder.Build(request);

        Assert.DoesNotContain("-auto-orient", arguments);
    }

    [Fact]
    public void Redimensionne_en_conservant_le_ratio_par_defaut()
    {
        var request = Request("photo.jpg", "photo.png", new ImageOptions { Width = 800, Height = 600 });

        var arguments = _builder.Build(request);
        var index = arguments.ToList().IndexOf("-resize");

        Assert.True(index >= 0);
        Assert.Equal("800x600", arguments[index + 1]);
    }

    [Fact]
    public void Force_les_dimensions_exactes_quand_le_ratio_n_est_pas_a_conserver()
    {
        var request = Request("photo.jpg", "photo.png", new ImageOptions
        {
            Width = 800,
            Height = 600,
            PreserveAspectRatio = false,
        });

        var arguments = _builder.Build(request);
        var index = arguments.ToList().IndexOf("-resize");

        Assert.Equal("800x600!", arguments[index + 1]);
    }

    [Fact]
    public void N_ajoute_pas_de_redimensionnement_sans_dimension_demandee()
    {
        var arguments = _builder.Build(Request("photo.jpg", "photo.png"));

        Assert.DoesNotContain("-resize", arguments);
    }

    [Fact]
    public void Efface_les_metadonnees_quand_demande()
    {
        var request = Request("photo.jpg", "photo.png", new ImageOptions { PreserveMetadata = false });

        Assert.Contains("-strip", _builder.Build(request));
    }

    [Fact]
    public void Conserve_les_metadonnees_par_defaut()
    {
        Assert.DoesNotContain("-strip", _builder.Build(Request("photo.jpg", "photo.png")));
    }

    [Fact]
    public void Passe_la_qualite_pour_un_jpeg()
    {
        var request = Request("photo.png", "photo.jpg", new ImageOptions { Quality = 85 });

        var arguments = _builder.Build(request);
        var index = arguments.ToList().IndexOf("-quality");

        Assert.Equal("85", arguments[index + 1]);
    }

    [Fact]
    public void Utilise_le_define_sans_perte_pour_un_webp_lossless()
    {
        var request = Request("photo.png", "photo.webp", new ImageOptions { Lossless = true });

        var arguments = _builder.Build(request);

        Assert.Contains("webp:lossless=true", arguments);
        Assert.DoesNotContain("-quality", arguments);
    }

    [Fact]
    public void Utilise_la_qualite_pour_un_webp_avec_perte()
    {
        var request = Request("photo.png", "photo.webp", new ImageOptions { Lossless = false, Quality = 80 });

        var arguments = _builder.Build(request);
        var index = arguments.ToList().IndexOf("-quality");

        Assert.Equal("80", arguments[index + 1]);
    }

    [Fact]
    public void N_applique_la_balance_des_blancs_automatique_que_sur_un_RAW()
    {
        // Sur une image deja developpee, -white-balance recalculerait une correction
        // qui n'a pas de sens : le reglage ne s'applique qu'au dematricage.
        var fromJpeg = Request("photo.jpg", "photo.png", new ImageOptions { WhiteBalance = RawWhiteBalance.Auto });

        Assert.DoesNotContain("-white-balance", _builder.Build(fromJpeg));
    }

    [Fact]
    public void Applique_la_balance_des_blancs_automatique_sur_un_RAW()
    {
        var request = Request("photo.cr2", "photo.jpg", new ImageOptions { WhiteBalance = RawWhiteBalance.Auto });

        Assert.Contains("-white-balance", _builder.Build(request));
    }

    [Theory]
    [InlineData(RawWhiteBalance.AsShot)]
    [InlineData(RawWhiteBalance.Camera)]
    public void N_ajoute_aucun_argument_pour_les_balances_qui_suivent_le_defaut_libraw(RawWhiteBalance mode)
    {
        // libraw applique par defaut la balance enregistree par le boitier : ces deux
        // modes n'ont donc rien a ajouter en ligne de commande.
        var request = Request("photo.cr2", "photo.jpg", new ImageOptions { WhiteBalance = mode });

        Assert.DoesNotContain("-white-balance", _builder.Build(request));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void Formate_les_dimensions_independamment_de_la_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var request = Request("photo.jpg", "photo.png", new ImageOptions { Width = 1920 });
            var arguments = _builder.Build(request);
            var index = arguments.ToList().IndexOf("-resize");

            Assert.Equal("1920x", arguments[index + 1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ConversionRequest Request(string input, string output, ConversionOptions? options = null)
    {
        var registry = FormatRegistry.Default;

        return new ConversionRequest
        {
            InputPath = input,
            OutputPath = output,
            SourceFormat = registry.GetByPath(input),
            TargetFormat = registry.GetByPath(output),
            Options = options ?? ConversionOptions.None,
        };
    }
}
