using ZiaConvert.Engines.Upscale;

namespace ZiaConvert.Engines.Tests.Upscale;

public sealed class RealEsrganProgressParserTests
{
    [Theory]
    [InlineData("0,00%", 0d)]
    [InlineData("25,00%", 25d)]
    [InlineData("50,00%", 50d)]
    [InlineData("97,92%", 97.92d)]
    public void Lit_le_pourcentage_a_virgule(string line, double expected)
    {
        // Confirme empiriquement sur une machine en fr-FR : l'outil imprime ses
        // pourcentages avec une virgule decimale, pas un point.
        var actual = RealEsrganProgressParser.TryParse(line);

        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value, precision: 2);
    }

    [Theory]
    [InlineData("0.00%")]
    [InlineData("25.00%")]
    public void Lit_aussi_le_pourcentage_a_point(string line)
    {
        // Au cas ou une machine dans une autre locale produirait un point plutot
        // qu'une virgule : les deux doivent etre acceptes sans configuration.
        Assert.NotNull(RealEsrganProgressParser.TryParse(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("input.jpg -> out.png done")]
    [InlineData("[0 NVIDIA GeForce RTX 4060 Ti]  queueC=2[8]  queueG=0[16]  queueT=1[2]")]
    [InlineData("invalid outputpath extension type")]
    public void Ignore_les_lignes_qui_ne_sont_pas_un_pourcentage(string line)
    {
        Assert.Null(RealEsrganProgressParser.TryParse(line));
    }

    [Fact]
    public void Ramene_une_valeur_hors_bornes_dans_0_100()
    {
        // Defensif : rien dans l'outil ne devrait produire ca, mais un pourcentage mal
        // forme ne doit jamais faire depasser la barre de progression.
        Assert.Equal(100d, RealEsrganProgressParser.TryParse("150,00%"));
        Assert.Equal(0d, RealEsrganProgressParser.TryParse("-10,00%"));
    }
}
