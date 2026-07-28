using ZiaConvert.Core.Model;
using ZiaConvert.Engines.FFmpeg;

namespace ZiaConvert.Engines.Tests.FFmpeg;

public sealed class ContainerCompatibilityTests
{
    [Theory]
    [InlineData("mp4", "h264", true)]
    [InlineData("mp4", "hevc", true)]
    [InlineData("mp4", "vp8", false)]
    [InlineData("webm", "vp9", true)]
    [InlineData("webm", "h264", false)]
    [InlineData("mkv", "h264", true)]
    [InlineData("mkv", "vp9", true)]
    [InlineData("mkv", "prores", true)]
    public void AcceptsVideoCodec_suit_la_table(string container, string codec, bool expected) =>
        Assert.Equal(expected, ContainerCompatibility.AcceptsVideoCodec(container, codec));

    [Theory]
    [InlineData("mp4", "aac", true)]
    [InlineData("mp4", "vorbis", false)]
    [InlineData("webm", "opus", true)]
    [InlineData("webm", "aac", false)]
    [InlineData("wav", "pcm_s16le", true)]
    [InlineData("wav", "mp3", false)]
    public void AcceptsAudioCodec_suit_la_table(string container, string codec, bool expected) =>
        Assert.Equal(expected, ContainerCompatibility.AcceptsAudioCodec(container, codec));

    [Fact]
    public void CanRemux_autorise_mp4_vers_mkv()
    {
        // Le cas le plus frequent : MKV accepte tout, la conversion se resume a reecrire
        // l'enveloppe. C'est la difference entre deux secondes et dix minutes.
        var source = Media(("h264", MediaStreamKind.Video), ("aac", MediaStreamKind.Audio));

        Assert.True(ContainerCompatibility.CanRemux("mkv", source, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void CanRemux_refuse_h264_vers_webm_en_l_expliquant()
    {
        var source = Media(("h264", MediaStreamKind.Video), ("aac", MediaStreamKind.Audio));

        Assert.False(ContainerCompatibility.CanRemux("webm", source, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("h264", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanRemux_refuse_quand_seul_l_audio_est_incompatible()
    {
        // La video passe, mais pas le son : il faut quand meme reencoder.
        var source = Media(("h264", MediaStreamKind.Video), ("vorbis", MediaStreamKind.Audio));

        Assert.False(ContainerCompatibility.CanRemux("mp4", source, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("vorbis", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanRemux_refuse_un_fichier_sans_flux()
    {
        var empty = new MediaInfo { Streams = [] };

        Assert.False(ContainerCompatibility.CanRemux("mkv", empty, out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void CanRemux_refuse_un_conteneur_inconnu()
    {
        // En cas de doute on reencode : lent mais correct, plutot qu'un fichier illisible.
        var source = Media(("h264", MediaStreamKind.Video));

        Assert.False(ContainerCompatibility.CanRemux("conteneur-invente", source, out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void CanRemux_ignore_les_sous_titres()
    {
        // Les pistes de sous-titres sont gerees a part par le constructeur d'arguments :
        // elles ne doivent pas a elles seules interdire une copie de flux.
        var source = Media(
            ("h264", MediaStreamKind.Video),
            ("aac", MediaStreamKind.Audio),
            ("subrip", MediaStreamKind.Subtitle));

        Assert.True(ContainerCompatibility.CanRemux("mkv", source, out _));
    }

    private static MediaInfo Media(params (string Codec, MediaStreamKind Kind)[] streams) => new()
    {
        Duration = TimeSpan.FromSeconds(10),
        Streams = [.. streams.Select((s, i) => new MediaStreamInfo
        {
            Index = i,
            Kind = s.Kind,
            CodecName = s.Codec,
        })],
    };
}
