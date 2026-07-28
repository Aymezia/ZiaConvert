using System.Globalization;
using ZiaConvert.Core.Processes;

namespace ZiaConvert.Core.Tests.Processes;

public sealed class ArgumentBuilderTests
{
    [Fact]
    public void Add_conserve_l_ordre_et_separe_drapeau_et_valeur()
    {
        var arguments = new ArgumentBuilder()
            .Add("-i")
            .Add("entree.mp4")
            .Add("-c:v", "libx264")
            .Build();

        Assert.Equal(["-i", "entree.mp4", "-c:v", "libx264"], arguments);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Add_formate_les_decimaux_en_culture_invariante(string culture)
    {
        // Sur une machine francaise, un formatage sensible a la culture produirait « 29,97 »,
        // que ffmpeg rejette. C'est une source de bogue classique et silencieuse.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var arguments = new ArgumentBuilder().Add("-r", 29.97d).Build();

            Assert.Equal(["-r", "29.97"], arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Add_formate_une_duree_en_secondes_decimales()
    {
        var arguments = new ArgumentBuilder()
            .Add("-ss", TimeSpan.FromSeconds(90.5))
            .Build();

        Assert.Equal(["-ss", "90.5"], arguments);
    }

    [Fact]
    public void AddIf_n_ajoute_rien_quand_la_condition_est_fausse()
    {
        var arguments = new ArgumentBuilder()
            .AddIf(false, "-an")
            .AddIf(true, "-vn")
            .Build();

        Assert.Equal(["-vn"], arguments);
    }

    [Fact]
    public void AddIfNotNull_ignore_les_valeurs_absentes()
    {
        var arguments = new ArgumentBuilder()
            .AddIfNotNull("-crf", (int?)null)
            .AddIfNotNull("-b:v", (long?)2_000_000)
            .Build();

        Assert.Equal(["-b:v", "2000000"], arguments);
    }

    [Fact]
    public void ToString_entoure_de_guillemets_les_arguments_a_espaces()
    {
        var command = new ArgumentBuilder()
            .Add("-i")
            .Add(@"C:\Mes videos\clip.mp4")
            .Add("-vf", "scale=1920:-1")
            .ToString();

        Assert.Equal(@"-i ""C:\Mes videos\clip.mp4"" -vf scale=1920:-1", command);
    }

    [Fact]
    public void Build_ne_modifie_pas_les_arguments_recus()
    {
        // L'echappement est delegue a ProcessStartInfo.ArgumentList : le constructeur ne
        // doit donc jamais toucher au contenu, sous peine de doubler l'echappement.
        const string filter = @"scale=480:-1,split[a][b];[a]palettegen[p];[b][p]paletteuse";

        var arguments = new ArgumentBuilder().Add("-vf", filter).Build();

        Assert.Equal(filter, arguments[1]);
    }
}
