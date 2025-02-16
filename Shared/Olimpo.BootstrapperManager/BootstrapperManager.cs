using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Olimpo;

public class BootstrapperManager : IBootstrapperManager
{
    private readonly IEnumerable<IBootstrapper> _bootstrappers;
    private readonly ILogger<BootstrapperManager> _logger;

    private int _moduleBootstrapped = 0;

    private int _moduleCount;

    public int ModulesCount { get; }

    public Subject<bool> AllModulesBootstrapped { get; }

    public Subject<bool> ModuleBootstrapped { get; }

    public BootstrapperManager(
        IEnumerable<IBootstrapper> bootstrappers,
        ILogger<BootstrapperManager> logger)
    {
        this._bootstrappers = bootstrappers;
        this._logger = logger;

        this.AllModulesBootstrapped = new Subject<bool>();
        this.ModuleBootstrapped = new Subject<bool>();
        this.ModulesCount = this._bootstrappers.Count();
    }

    public async Task Start()
    {
        this._moduleCount = this._bootstrappers.Count();
        this._moduleBootstrapped = 0 ;

        var orderedBootstrappers = this._bootstrappers.OrderBy(x => x.Priority);
        foreach(var module in orderedBootstrappers)
        {
            module.BootstrapFinished.Subscribe(this.OnBootstrapFinished);

            this._logger.LogInformation($"Starting {module.GetType().Name}...");

            try
            {
                await module.Startup();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, $"Error starting {module.GetType().Name}...");
                throw;
            }
        }    
    }

    public void OnBootstrapFinished(bool isFinished)
    {
        if (isFinished)
        {
            this._moduleBootstrapped ++;
            this.ModuleBootstrapped.OnNext(true);
        }

        if (this._moduleBootstrapped >= this._moduleCount)
        {
            this.AllModulesBootstrapped.OnNext(true);
        }
    }
}
