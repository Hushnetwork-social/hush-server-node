// using Olimpo;

// namespace HushServerNode.ApplicationSettingsService;

// public class ApplicationSettingsBootstrapper : IBootstrapper
// {
//     private readonly IApplicationSettingsService _applicationSettingsService;

//     public int Priority { get; set; } = 0;

//     public ApplicationSettingsBootstrapper(IApplicationSettingsService applicationSettingsService)
//     {
//         this._applicationSettingsService = applicationSettingsService;
//     }

//     public void Shutdown()
//     {
//         // there is nothing to Shutdown in the service.
//     }

//     public Task Startup()
//     {
//         this._applicationSettingsService.LoadSettings();

//         return Task.CompletedTask;
//     }
// }
