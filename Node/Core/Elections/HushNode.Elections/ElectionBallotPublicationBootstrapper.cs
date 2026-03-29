using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Elections;

public sealed class ElectionBallotPublicationBootstrapper : IBootstrapper
{
    private readonly ILogger<ElectionBallotPublicationBootstrapper> _logger;

    public ElectionBallotPublicationBootstrapper(
        IEventAggregator eventAggregator,
        ElectionBallotPublicationService ballotPublicationService,
        ILogger<ElectionBallotPublicationBootstrapper> logger)
    {
        _logger = logger;
        eventAggregator.Subscribe(ballotPublicationService);
    }

    public Subject<string> BootstrapFinished { get; } = new();

    public int Priority { get; set; } = 18;

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        _logger.LogInformation("[ElectionBallotPublicationBootstrapper] Ballot publication service subscribed.");
        BootstrapFinished.OnNext(nameof(ElectionBallotPublicationBootstrapper));
        return Task.CompletedTask;
    }
}
