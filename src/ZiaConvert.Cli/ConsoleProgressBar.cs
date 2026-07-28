using System.Globalization;
using ZiaConvert.Core.Model;

namespace ZiaConvert.Cli;

/// <summary>
/// Affiche l'avancement sur une ligne rafraichie en place.
/// </summary>
/// <remarks>
/// Tout part sur la sortie d'erreur : la sortie standard reste ainsi utilisable dans un
/// tube sans etre polluee par les retours chariot de la barre.
/// </remarks>
internal sealed class ConsoleProgressBar
{
    private const int BarWidth = 28;

    private readonly bool _enabled;
    private DateTimeOffset _lastDraw = DateTimeOffset.MinValue;
    private int _lastLength;

    public ConsoleProgressBar() =>

        // Redirigee, la sortie n'a pas de largeur de terminal ni de curseur : on se contente
        // alors des messages d'etape, sans animation.
        _enabled = !Console.IsErrorRedirected;

    public void Report(ConversionProgress progress)
    {
        if (!_enabled)
        {
            return;
        }

        // Au-dela de dix rafraichissements par seconde, l'affichage scintille sans rien
        // apporter, et le cout d'ecriture devient sensible sur les conversions rapides.
        var now = DateTimeOffset.UtcNow;

        if (progress.Stage != ConversionStage.Completed && now - _lastDraw < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _lastDraw = now;
        Draw(Compose(progress));
    }

    public void Clear()
    {
        if (_enabled && _lastLength > 0)
        {
            Console.Error.Write('\r' + new string(' ', _lastLength) + '\r');
            _lastLength = 0;
        }
    }

    private static string Compose(ConversionProgress progress)
    {
        if (progress.Percent is not { } percent)
        {
            var label = progress.Message ?? progress.Stage.ToString();
            return $"  {label}...";
        }

        var filled = (int)Math.Round(percent / 100d * BarWidth, MidpointRounding.AwayFromZero);
        var bar = new string('#', filled) + new string('.', BarWidth - filled);

        var parts = new List<string>
        {
            $"  [{bar}] {percent.ToString("00.0", CultureInfo.InvariantCulture)}%",
        };

        if (progress.Speed is { } speed)
        {
            parts.Add($"{speed.ToString("0.0", CultureInfo.InvariantCulture)}x");
        }

        if (progress.Eta is { } eta && eta > TimeSpan.Zero)
        {
            parts.Add($"reste {Format(eta)}");
        }

        return string.Join("  ", parts);
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private void Draw(string text)
    {
        // La ligne precedente est completee par des espaces : sans cela, un texte plus
        // court laisserait trainer la fin de l'affichage precedent.
        var padding = Math.Max(0, _lastLength - text.Length);

        Console.Error.Write('\r' + text + new string(' ', padding));
        _lastLength = text.Length;
    }
}
