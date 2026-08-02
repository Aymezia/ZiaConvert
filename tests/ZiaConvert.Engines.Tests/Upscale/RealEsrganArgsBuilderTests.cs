using ZiaConvert.Core.Options;
using ZiaConvert.Engines.Upscale;

namespace ZiaConvert.Engines.Tests.Upscale;

public sealed class RealEsrganArgsBuilderTests
{
    [Fact]
    public void Place_l_entree_et_la_sortie()
    {
        var arguments = Build("in.png", "out.png", "png");

        Assert.Equal("in.png", arguments[arguments.ToList().IndexOf("-i") + 1]);
        Assert.Equal("out.png", arguments[arguments.ToList().IndexOf("-o") + 1]);
    }

    [Fact]
    public void Impose_toujours_le_format_explicitement()
    {
        // L'outil valide lui-meme l'extension du chemin de sortie et refuse tout ce
        // qu'il ne reconnait pas : sans -f explicite, un chemin de travail au nom
        // inhabituel (prefixe cache, pas d'extension standard) serait rejete.
        var arguments = Build("in.png", "out.png", "webp");

        Assert.Contains("-f", arguments);
        Assert.Equal("webp", arguments[arguments.ToList().IndexOf("-f") + 1]);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void Accepte_les_trois_formats_geres_par_l_outil(string format)
    {
        var arguments = Build("in.png", "out.x", format);

        Assert.Equal(format, arguments[arguments.ToList().IndexOf("-f") + 1]);
    }

    [Fact]
    public void Retombe_sur_png_pour_un_format_non_gere()
    {
        // avif, heic, tiff... ne sont pas dans la liste jpg/png/webp de l'outil : PNG,
        // toujours accepte et sans perte, est le repli le plus sur.
        var arguments = Build("in.png", "out.x", "avif");

        Assert.Equal("png", arguments[arguments.ToList().IndexOf("-f") + 1]);
    }

    [Fact]
    public void Passe_le_modele_et_le_facteur()
    {
        var options = new UpscaleOptions { Model = "realesrgan-x4plus-anime", Factor = 2 };

        var arguments = Build("in.png", "out.png", "png", options);

        Assert.Equal("realesrgan-x4plus-anime", arguments[arguments.ToList().IndexOf("-n") + 1]);
        Assert.Equal("2", arguments[arguments.ToList().IndexOf("-s") + 1]);
    }

    [Fact]
    public void Passe_toujours_la_taille_de_tuile_meme_a_zero()
    {
        // 0 est la valeur « automatique » de l'outil, pas une absence de reglage :
        // omettre -t reviendrait au meme ici, mais le rendre explicite documente l'intention.
        var arguments = Build("in.png", "out.png", "png", new UpscaleOptions { TileSize = 0 });

        Assert.Contains("-t", arguments);
        Assert.Equal("0", arguments[arguments.ToList().IndexOf("-t") + 1]);
    }

    [Fact]
    public void N_ajoute_pas_d_index_gpu_par_defaut()
    {
        var arguments = Build("in.png", "out.png", "png", new UpscaleOptions { GpuId = null });

        Assert.DoesNotContain("-g", arguments);
    }

    [Fact]
    public void Passe_l_index_gpu_quand_precise()
    {
        var arguments = Build("in.png", "out.png", "png", new UpscaleOptions { GpuId = 1 });

        Assert.Equal("1", arguments[arguments.ToList().IndexOf("-g") + 1]);
    }

    [Fact]
    public void Ajoute_le_mode_verbeux_pour_la_conversion_reelle()
    {
        var arguments = RealEsrganArgsBuilder.Build("in.png", "out.png", "png", new UpscaleOptions(), verbose: true);

        Assert.Contains("-v", arguments);
    }

    [Fact]
    public void Omet_le_mode_verbeux_pour_la_calibration()
    {
        // La calibration ne lit que le temps ecoule du processus : le detail de la
        // progression ne l'interesse pas.
        var arguments = RealEsrganArgsBuilder.Build("in.png", "out.png", "png", new UpscaleOptions(), verbose: false);

        Assert.DoesNotContain("-v", arguments);
    }

    private static IReadOnlyList<string> Build(string input, string output, string format, UpscaleOptions? options = null) =>
        RealEsrganArgsBuilder.Build(input, output, format, options ?? new UpscaleOptions(), verbose: true);
}
