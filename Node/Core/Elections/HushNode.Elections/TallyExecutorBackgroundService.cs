using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed class TallyExecutorBackgroundService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    IElectionLifecycleService electionLifecycleService,
    ILogger<TallyExecutorBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly ILogger<TallyExecutorBackgroundService> _logger = logger;
    private readonly string _leaseHolderId = $"tally-executor:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Tally executor background service started with lease holder {LeaseHolderId}",
            _leaseHolderId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tally executor background loop failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Tally executor background service stopped for lease holder {LeaseHolderId}",
            _leaseHolderId);
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var runnableJobIds = await GetRunnableCloseCountingJobIdsAsync(cancellationToken);
        foreach (var closeCountingJobId in runnableJobIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Tally executor picked up close-counting job {CloseCountingJobId} with lease holder {LeaseHolderId}",
                closeCountingJobId,
                _leaseHolderId);

            var result = await _electionLifecycleService.ExecuteCloseCountingJobAsync(
                new ExecuteElectionCloseCountingJobRequest(
                    closeCountingJobId,
                    _leaseHolderId));

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Tally executor completed close-counting job {CloseCountingJobId} for election {ElectionId}",
                    closeCountingJobId,
                    result.Election?.ElectionId);
                continue;
            }

            if (!result.IsSuccess && result.ErrorCode != ElectionCommandErrorCode.Conflict)
            {
                _logger.LogWarning(
                    "Tally executor failed for close-counting job {CloseCountingJobId}: {ErrorCode} {ErrorMessage}",
                    closeCountingJobId,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
        }
    }

    private async Task<IReadOnlyList<Guid>> GetRunnableCloseCountingJobIdsAsync(CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var electionIds = await repository.GetClosedElectionIdsAwaitingTallyReadyAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var runnableJobs = new List<ElectionCloseCountingJobRecord>();
        foreach (var electionId in electionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var closeCountingJobs = await repository.GetCloseCountingJobsAsync(electionId);
            runnableJobs.AddRange(closeCountingJobs.Where(x =>
                x.Status is ElectionCloseCountingJobStatus.ThresholdReached
                    or ElectionCloseCountingJobStatus.Running
                    or ElectionCloseCountingJobStatus.Publishing));
        }

        return runnableJobs
            .OrderBy(x => x.ThresholdReachedAt ?? DateTime.MaxValue)
            .ThenBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToArray();
    }
}
