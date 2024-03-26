using System.Net;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using HushServerNode.ApplicationSettings;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.ServerService
{
    public class ServerBootstrapper : IBootstrapper
    {
        private readonly IApplicationSettingsService _applicationSettingsService;
        private readonly IServer _server;
        private readonly ITcpServerService _tcpServerService;
        private readonly ILogger<ServerBootstrapper> _logger;

        public Subject<bool> BootstrapFinished { get; }

        public int Priority { get; set; } = 10;

        public ServerBootstrapper(
            IApplicationSettingsService applicationSettingsService,
            IServer server,
            ITcpServerService tcpServerService,
            ILogger<ServerBootstrapper> logger)
        {
            this._applicationSettingsService = applicationSettingsService;
            this._server = server;
            this._tcpServerService = tcpServerService;
            this._logger = logger;

            this.BootstrapFinished = new Subject<bool>();
        }

        public void Shutdown()
        {
            this._logger.LogInformation("Stopping TCP Server...");
        }

        public async Task Startup()
        {
            this._logger.LogInformation("Starting TCP Server...");
            this.BootstrapFinished.OnNext(true);
            await this._server.Start(IPAddress.Any, this._applicationSettingsService.ServerInfo.ListeningPort);
        }
    }
}