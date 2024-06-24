using System;
using System.IO;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading.Tasks;
using HushServerNode.ApplicationSettings.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.ApplicationSettings;

public class ApplicationSettingsService : IApplicationSettingsService, IBootstrapper
{
    private const string ApplicationSettingsFileName = "ApplicationSettings.json";
    private ServerInfo _serverInfo = new ServerInfo();
    private StackerInfo _stackerInfo = new StackerInfo();

    private readonly ILogger<ApplicationSettingsService> _logger;

    public int Priority { get; set; } = 0;

    public ServerInfo ServerInfo { get => this._serverInfo; }
    
    public StackerInfo StackerInfo { get => this._stackerInfo; }

    public Subject<bool> BootstrapFinished { get; }

    public string ConnectionString { get; private set;} = string.Empty;

    public ApplicationSettingsService(
        ILogger<ApplicationSettingsService> logger)
    {
        this._logger = logger;

        this.BootstrapFinished = new Subject<bool>();
    }

    public void LoadSettings()
    {
        this._logger.LogInformation("Load ApplicationSettings");

        var executingAssemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (string.IsNullOrEmpty(executingAssemblyFolder))
        {
            this._logger.LogError("Could not get the executing assembly folder. Settings aren't going to be load.");
            return;
        }

        var applicationSettingsFilePath = Path.Combine(
                executingAssemblyFolder, 
                "ApplicationSettings.json");

        var config = new ConfigurationBuilder()
            .AddJsonFile(applicationSettingsFilePath)
            .AddEnvironmentVariables()
            .Build();

        this.LoadServerInfo(config);
        this.LoadStakerInfo(config);
        this.LoadConnectionString(config);
    }

    private void LoadServerInfo(IConfigurationRoot config)
    {
        var serverInfo = config
            .GetRequiredSection("ServerInfo")
            .Get<ServerInfo>();
        
        if (serverInfo == null)
        {
            throw new InvalidOperationException("Missing server information in ApplicationSetting.json");
        }

        this._serverInfo = serverInfo;
    }

    private void LoadStakerInfo(IConfigurationRoot config)
    {
        var stackerInfo = config
            .GetRequiredSection("StackerInfo")
            .Get<StackerInfo>();

        if (stackerInfo == null)
        {
            throw new InvalidOperationException("Missing stacker information in ApplicationSetting.json");
        }

        this._stackerInfo = stackerInfo;
    }

    private void LoadConnectionString(IConfigurationRoot config)
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST");
        var db = Environment.GetEnvironmentVariable("POSTGRES_DB");
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        this.ConnectionString = string.Format("Host={0}; Database={1}; Username={2}; Password={3}", host, db, user, password);
    }

    public Task Startup()
    {
        this.LoadSettings();

        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        
    }
}
