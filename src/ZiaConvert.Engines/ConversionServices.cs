using Microsoft.Extensions.Logging;
using ZiaConvert.Core.Abstractions;
using ZiaConvert.Core.Jobs;
using ZiaConvert.Core.Processes;
using ZiaConvert.Core.Routing;
using ZiaConvert.Core.Tools;
using ZiaConvert.Engines.FFmpeg;

namespace ZiaConvert.Engines;

/// <summary>
/// Assemble les composants de conversion.
/// </summary>
/// <remarks>
/// Point de composition unique, partage par la ligne de commande et l'interface graphique.
/// Volontairement sans conteneur d'injection : le graphe est petit, fige, et le lire d'un
/// seul coup d'œil vaut mieux qu'une configuration eparpillee.
/// </remarks>
public sealed class ConversionServices
{
    private ConversionServices(
        IProcessRunner processRunner,
        IEngineLocator locator,
        IMediaProbe probe,
        HardwareDetector hardware,
        IReadOnlyList<IConversionEngine> engines,
        ConversionRouter router,
        ConversionExecutor executor,
        FormatRegistry formats)
    {
        ProcessRunner = processRunner;
        Locator = locator;
        Probe = probe;
        Hardware = hardware;
        Engines = engines;
        Router = router;
        Executor = executor;
        Formats = formats;
    }

    public IProcessRunner ProcessRunner { get; }

    public IEngineLocator Locator { get; }

    public IMediaProbe Probe { get; }

    public HardwareDetector Hardware { get; }

    public IReadOnlyList<IConversionEngine> Engines { get; }

    public ConversionRouter Router { get; }

    public ConversionExecutor Executor { get; }

    public FormatRegistry Formats { get; }

    public static ConversionServices Create(ILoggerFactory? loggerFactory = null)
    {
        var processRunner = new ProcessRunner(loggerFactory?.CreateLogger<ProcessRunner>());
        var locator = new ToolLocator();
        var probe = new FFprobeService(processRunner, locator);
        var hardware = new HardwareDetector(processRunner, locator, loggerFactory?.CreateLogger<HardwareDetector>());

        IReadOnlyList<IConversionEngine> engines =
        [
            new FFmpegEngine(processRunner, locator, probe, hardware, loggerFactory?.CreateLogger<FFmpegEngine>()),
        ];

        var formats = FormatRegistry.Default;
        var router = new ConversionRouter(engines, probe, formats, loggerFactory?.CreateLogger<ConversionRouter>());
        var executor = new ConversionExecutor(router, loggerFactory?.CreateLogger<ConversionExecutor>());

        return new ConversionServices(processRunner, locator, probe, hardware, engines, router, executor, formats);
    }

    /// <summary>Cree une file d'attente branchee sur ces services.</summary>
    public JobQueue CreateQueue(ConcurrencyPolicy? policy = null, ILoggerFactory? loggerFactory = null) =>
        new(Executor, policy, loggerFactory?.CreateLogger<JobQueue>());
}
