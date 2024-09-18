using System.Net;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.ServerService
{
    public class ServerBootstrapper : IBootstrapper
    {
        private readonly IServer _server;
        private readonly ILogger<ServerBootstrapper> _logger;

        public Subject<bool> BootstrapFinished { get; }

        public int Priority { get; set; } = 10;

        public ServerBootstrapper(
            IServer server,
            ILogger<ServerBootstrapper> logger)
        {
            this._server = server;
            this._logger = logger;

            this.BootstrapFinished = new Subject<bool>();
        }

        public void Shutdown()
        {
            this._logger.LogInformation("Stopping TCP Server...");
        }

        public Task Startup()
        {
            this._logger.LogInformation("Starting TCP Server...");
            this.BootstrapFinished.OnNext(true);
            return Task.CompletedTask;
        }
    }
}