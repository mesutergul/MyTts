// namespace MyTts.Services
// {
//     public class HostedServiceWrapper : IHostedService
//     {
//         private readonly TtsManagerService _ttsManager;

//         public HostedServiceWrapper(TtsManagerService ttsManager)
//         {
//             _ttsManager = ttsManager;
//         }

//         public Task StartAsync(CancellationToken cancellationToken)
//         {
//             // Initialize TtsManager here if needed
//             return Task.CompletedTask;
//         }

//         public async Task StopAsync(CancellationToken cancellationToken)
//         {
//             // Dispose TtsManager properly when the application is shutting down
//             await _ttsManager.DisposeAsync();
//         }
//     }
// }
