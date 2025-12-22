using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Reactions;

public class ReactionsBootstrapper : IBootstrapper
{
    private readonly FeedCreatedCommitmentHandler _feedCreatedCommitmentHandler;
    private readonly IUserCommitmentService _userCommitmentService;
    private readonly ILogger<ReactionsBootstrapper> _logger;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 15;  // Run after Feeds module

    public ReactionsBootstrapper(
        IEventAggregator eventAggregator,
        FeedCreatedCommitmentHandler feedCreatedCommitmentHandler,
        IUserCommitmentService userCommitmentService,
        ILogger<ReactionsBootstrapper> logger)
    {
        _feedCreatedCommitmentHandler = feedCreatedCommitmentHandler;
        _userCommitmentService = userCommitmentService;
        _logger = logger;

        eventAggregator.Subscribe(this);
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        _logger.LogInformation("[ReactionsBootstrapper] Starting Reactions module...");

        // Log the local user's commitment for debugging
        var commitment = _userCommitmentService.GetLocalUserCommitment();
        _logger.LogInformation(
            "[ReactionsBootstrapper] Local user commitment: {Commitment}",
            Convert.ToHexString(commitment)[..32] + "...");

        // The FeedCreatedCommitmentHandler is already subscribed via constructor injection
        _logger.LogInformation("[ReactionsBootstrapper] FeedCreatedCommitmentHandler initialized");

        await Task.CompletedTask;
        this.BootstrapFinished.OnNext("Reactions");
    }
}
