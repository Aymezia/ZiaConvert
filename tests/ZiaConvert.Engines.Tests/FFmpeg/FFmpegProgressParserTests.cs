using ZiaConvert.Engines.FFmpeg;

namespace ZiaConvert.Engines.Tests.FFmpeg;

public sealed class FFmpegProgressParserTests
{
    [Fact]
    public void N_emet_rien_avant_la_fin_du_bloc()
    {
        var parser = new FFmpegProgressParser();

        Assert.Null(parser.Feed("frame=120"));
        Assert.Null(parser.Feed("fps=30.0"));
        Assert.Null(parser.Feed("out_time_us=4000000"));

        // Seule la ligne « progress= » cloture un bloc et rend un releve complet.
        Assert.NotNull(parser.Feed("progress=continue"));
    }

    [Fact]
    public void Interprete_out_time_us_en_microsecondes()
    {
        // Piege classique : ffmpeg emet aussi « out_time_ms », qui est en realite en
        // microsecondes. Le prendre pour des millisecondes rendrait un avancement mille
        // fois trop rapide, et une barre qui saute a 100 % en une seconde.
        var parser = new FFmpegProgressParser();

        parser.Feed("out_time_us=90000000");
        var snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.FromSeconds(90), snapshot.OutTime);
    }

    [Fact]
    public void Retombe_sur_out_time_quand_les_microsecondes_manquent()
    {
        var parser = new FFmpegProgressParser();

        parser.Feed("out_time=00:01:30.500000");
        var snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.FromSeconds(90.5), snapshot.OutTime);
    }

    [Theory]
    [InlineData("2.53x", 2.53)]
    [InlineData("0.98x", 0.98)]
    [InlineData("15.2x", 15.2)]
    public void Lit_la_vitesse_sans_son_suffixe(string raw, double expected)
    {
        var parser = new FFmpegProgressParser();

        parser.Feed($"speed={raw}");
        var snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(expected, snapshot.Speed);
    }

    [Fact]
    public void Tolere_une_vitesse_indisponible()
    {
        // ffmpeg rend « N/A » tant qu'il n'a pas assez d'echantillons pour estimer.
        var parser = new FFmpegProgressParser();

        parser.Feed("speed=N/A");
        var snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Speed);
    }

    [Fact]
    public void Signale_le_dernier_releve()
    {
        var parser = new FFmpegProgressParser();

        Assert.False(parser.Feed("progress=continue")!.IsFinal);
        Assert.True(parser.Feed("progress=end")!.IsFinal);
    }

    [Fact]
    public void Repart_a_zero_entre_deux_blocs()
    {
        // Sans remise a zero, un champ absent du second bloc conserverait la valeur du
        // premier, et l'avancement paraitrait fige au lieu de devenir indetermine.
        var parser = new FFmpegProgressParser();

        parser.Feed("total_size=1024");
        parser.Feed("progress=continue");

        var second = parser.Feed("progress=continue");

        Assert.NotNull(second);
        Assert.Null(second.TotalSize);
    }

    [Fact]
    public void Ignore_les_lignes_sans_egal()
    {
        var parser = new FFmpegProgressParser();

        Assert.Null(parser.Feed(""));
        Assert.Null(parser.Feed("ligne quelconque"));
        Assert.Null(parser.Feed("=valeur-sans-cle"));
    }

    [Fact]
    public void Lit_le_numero_d_image_et_la_taille()
    {
        var parser = new FFmpegProgressParser();

        parser.Feed("frame=1234");
        parser.Feed("total_size=987654");
        var snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(1234L, snapshot.Frame);
        Assert.Equal(987654L, snapshot.TotalSize);
    }
}
