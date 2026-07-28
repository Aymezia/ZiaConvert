using System.Globalization;
using System.Text;

namespace ZiaConvert.Core.Processes;

/// <summary>
/// Construit une liste d'arguments de ligne de commande.
/// </summary>
/// <remarks>
/// Aucun echappement n'est fait ici, et c'est voulu : les arguments partent dans
/// <c>ProcessStartInfo.ArgumentList</c>, qui applique les regles de la plateforme.
/// Un argument vaut donc exactement ce qu'on lui donne, espaces et guillemets compris.
/// <para>
/// Les surcharges numeriques formatent en culture invariante. C'est indispensable :
/// sur une machine francaise, <c>29.97</c> deviendrait sinon <c>29,97</c> et ffmpeg
/// refuserait la valeur.
/// </para>
/// </remarks>
public sealed class ArgumentBuilder
{
    private readonly List<string> _arguments = [];

    public int Count => _arguments.Count;

    public ArgumentBuilder Add(string argument)
    {
        _arguments.Add(argument);
        return this;
    }

    public ArgumentBuilder Add(string flag, string value)
    {
        _arguments.Add(flag);
        _arguments.Add(value);
        return this;
    }

    public ArgumentBuilder Add(string flag, int value) =>
        Add(flag, value.ToString(CultureInfo.InvariantCulture));

    public ArgumentBuilder Add(string flag, long value) =>
        Add(flag, value.ToString(CultureInfo.InvariantCulture));

    public ArgumentBuilder Add(string flag, double value) =>
        Add(flag, value.ToString("0.####", CultureInfo.InvariantCulture));

    /// <summary>Formate en secondes decimales, la forme acceptee partout par ffmpeg.</summary>
    public ArgumentBuilder Add(string flag, TimeSpan value) =>
        Add(flag, value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));

    public ArgumentBuilder AddRange(IEnumerable<string> arguments)
    {
        _arguments.AddRange(arguments);
        return this;
    }

    public ArgumentBuilder AddIf(bool condition, string argument) =>
        condition ? Add(argument) : this;

    public ArgumentBuilder AddIf(bool condition, string flag, string value) =>
        condition ? Add(flag, value) : this;

    /// <summary>N'ajoute le couple que si la valeur est renseignee.</summary>
    public ArgumentBuilder AddIfNotNull(string flag, string? value) =>
        value is null ? this : Add(flag, value);

    public ArgumentBuilder AddIfNotNull(string flag, int? value) =>
        value is null ? this : Add(flag, value.Value);

    public ArgumentBuilder AddIfNotNull(string flag, long? value) =>
        value is null ? this : Add(flag, value.Value);

    public ArgumentBuilder AddIfNotNull(string flag, double? value) =>
        value is null ? this : Add(flag, value.Value);

    public ArgumentBuilder AddIfNotNull(string flag, TimeSpan? value) =>
        value is null ? this : Add(flag, value.Value);

    public IReadOnlyList<string> Build() => _arguments.AsReadOnly();

    /// <summary>
    /// Rend la commande sous une forme copiable dans un terminal. Reserve aux journaux
    /// et au diagnostic : ce n'est jamais cette chaine qui est executee.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();

        foreach (var argument in _arguments)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            var needsQuotes = argument.Length == 0 || argument.AsSpan().ContainsAny(' ', '\t', '"');
            if (needsQuotes)
            {
                builder.Append('"').Append(argument.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                builder.Append(argument);
            }
        }

        return builder.ToString();
    }
}
