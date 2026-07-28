using ZiaConvert.Core.Processes;

namespace ZiaConvert.Core.Tests.Processes;

/// <summary>
/// Fabrique des commandes systeme au comportement connu, pour eprouver
/// <see cref="ProcessRunner" /> sans dependre d'un outil externe au projet.
/// </summary>
internal static class TestCommands
{
    private static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Ecrit une ligne sur la sortie standard puis se termine avec le code 0.</summary>
    public static ProcessRequest Echo(string text) => Shell($"echo {text}");

    /// <summary>Ecrit une ligne sur la sortie d'erreur puis se termine avec le code 0.</summary>
    public static ProcessRequest EchoError(string text) =>
        Shell(IsWindows ? $"echo {text} 1>&2" : $"echo {text} 1>&2");

    /// <summary>Ecrit sur les deux flux, puis se termine avec le code demande.</summary>
    public static ProcessRequest EchoBothThenExit(string outText, string errText, int exitCode) =>
        Shell(IsWindows
            ? $"echo {outText}& echo {errText} 1>&2& exit {exitCode}"
            : $"echo {outText}; echo {errText} 1>&2; exit {exitCode}");

    /// <summary>Se termine immediatement avec le code demande, sans rien ecrire.</summary>
    public static ProcessRequest Exit(int exitCode) => Shell($"exit {exitCode}");

    /// <summary>Emet <paramref name="count" /> lignes numerotees sur la sortie standard.</summary>
    public static ProcessRequest EchoManyLines(int count) =>
        Shell(IsWindows
            ? $"for /l %i in (1,1,{count}) do @echo line%i"
            : $"i=1; while [ $i -le {count} ]; do echo line$i; i=$((i+1)); done");

    /// <summary>Reste actif plusieurs secondes en ignorant son entree standard.</summary>
    public static ProcessRequest LongRunning() =>
        Shell(IsWindows ? "ping -n 30 127.0.0.1 > nul" : "sleep 30");

    /// <summary>Chemin d'un executable qui n'existe pas.</summary>
    public static ProcessRequest Missing() => new()
    {
        FileName = "zia-executable-qui-nexiste-pas",
        Arguments = [],
    };

    private static ProcessRequest Shell(string command) => new()
    {
        FileName = IsWindows ? "cmd.exe" : "/bin/sh",
        Arguments = IsWindows ? ["/c", command] : ["-c", command],
    };
}
