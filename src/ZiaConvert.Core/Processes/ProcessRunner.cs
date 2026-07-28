using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZiaConvert.Core.Processes;

public interface IProcessRunner
{
    /// <summary>
    /// Lance le processus et restitue ses lignes de sortie au fil de l'eau, les deux flux
    /// entremeles dans leur ordre d'arrivee.
    /// </summary>
    /// <exception cref="ProcessExecutionException">Le processus s'est termine avec un code non nul.</exception>
    /// <exception cref="OperationCanceledException">L'annulation a ete demandee.</exception>
    IAsyncEnumerable<ProcessOutputLine> StreamAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute le processus et collecte toute sa sortie. Contrairement a
    /// <see cref="StreamAsync" />, un code de sortie non nul est rendu dans le resultat
    /// plutot que leve : pratique pour les commandes de sondage ou de detection.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IProcessRunner" />
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner>? logger = null) =>
        _logger = logger ?? NullLogger<ProcessRunner>.Instance;

    public async IAsyncEnumerable<ProcessOutputLine> StreamAsync(
        ProcessRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var process = CreateProcess(request);

        var channel = Channel.CreateUnbounded<ProcessOutputLine>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        // stdout et stderr signalent chacun leur fin par un evenement a Data == null.
        // Le canal ne se ferme qu'une fois les deux epuises : le fermer sur le premier
        // ferait perdre les dernieres lignes de l'autre, justement celles qui portent l'erreur.
        var openStreams = 2;

        void Emit(ProcessStreamKind kind, string? data)
        {
            if (data is not null)
            {
                channel.Writer.TryWrite(new ProcessOutputLine(kind, data));
            }
            else if (Interlocked.Decrement(ref openStreams) == 0)
            {
                channel.Writer.TryComplete();
            }
        }

        process.OutputDataReceived += (_, e) => Emit(ProcessStreamKind.StandardOutput, e.Data);
        process.ErrorDataReceived += (_, e) => Emit(ProcessStreamKind.StandardError, e.Data);

        Start(process, request);

        var errorTail = new Queue<string>();

        await using (cancellationToken.Register(() => _ = StopAsync(process, request)).ConfigureAwait(false))
        {
            // Le drainage se fait volontairement sans jeton d'annulation : on veut lire la
            // sortie jusqu'au bout, y compris pendant un arret, avant de lever.
            await foreach (var line in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (line.IsError && request.ErrorTailLines > 0)
                {
                    errorTail.Enqueue(line.Text);
                    if (errorTail.Count > request.ErrorTailLines)
                    {
                        errorTail.Dequeue();
                    }
                }

                yield return line;
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new ProcessExecutionException(request.FileName, process.ExitCode, [.. errorTail]);
        }
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var standardOutput = new List<string>();
        var standardError = new List<string>();

        try
        {
            await foreach (var line in StreamAsync(request, cancellationToken).ConfigureAwait(false))
            {
                (line.IsError ? standardError : standardOutput).Add(line.Text);
            }
        }
        catch (ProcessExecutionException ex)
        {
            return new ProcessResult(ex.ExitCode, standardOutput, standardError);
        }

        return new ProcessResult(0, standardOutput, standardError);
    }

    private static Process CreateProcess(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // L'entree standard n'est ouverte que si on compte s'en servir pour l'arret propre :
            // certains outils changent de comportement quand stdin n'est pas un terminal.
            RedirectStandardInput = request.GracefulStopInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.WorkingDirectory is { } workingDirectory)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (request.EnvironmentVariables is { } variables)
        {
            foreach (var (key, value) in variables)
            {
                startInfo.Environment[key] = value;
            }
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private void Start(Process process, ProcessRequest request)
    {
        _logger.LogDebug("Lancement : {FileName} {Arguments}", request.FileName, string.Join(' ', request.Arguments));

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Executable introuvable ou non executable : on uniformise avec les autres echecs
            // pour que les moteurs n'aient qu'un seul type d'exception a traiter.
            throw new ProcessExecutionException(request.FileName, -1, [ex.Message]);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// Arrete le processus a l'annulation : d'abord la demande polie sur l'entree standard,
    /// puis la terminaison forcee si le delai est depasse. Un <c>Kill</c> immediat laisserait
    /// un fichier de sortie tronque et donc illisible.
    /// </summary>
    private async Task StopAsync(Process process, ProcessRequest request)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (request.GracefulStopInput is { } stopInput)
            {
                try
                {
                    await process.StandardInput.WriteAsync(stopInput).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // Le processus a ferme son entree de lui-meme : on passe a la terminaison.
                }

                using var timeout = new CancellationTokenSource(request.GracefulStopTimeout);

                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "{FileName} n'a pas repondu a la demande d'arret en {Timeout} s, terminaison forcee.",
                        Path.GetFileName(request.FileName),
                        request.GracefulStopTimeout.TotalSeconds);
                }
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Course normale : le processus s'est termine entre le test et l'action.
            _logger.LogDebug(ex, "Arret de {FileName} : le processus etait deja termine.", request.FileName);
        }
    }
}
