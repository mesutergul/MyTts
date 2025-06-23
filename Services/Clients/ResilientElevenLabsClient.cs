using ElevenLabs;
using ElevenLabs.TextToSpeech;
using Polly;
using MyTts.Helpers;
using ElevenLabs.Voices;
using System.Net.Sockets;

namespace MyTts.Services.Clients
{
    public class ResilientElevenLabsClient : IAsyncDisposable
    {
        private readonly ElevenLabsClient _client;
        private readonly ResiliencePipeline<ElevenLabs.VoiceClip> _voiceClipPipeline;
        private readonly ResiliencePipeline<Voice> _voicePipeline;
        private readonly ILogger<ResilientElevenLabsClient> _logger;
        private readonly CombinedRateLimiter _rateLimiter;
        private static readonly ResiliencePropertyKey<string> OperationKey = new("OperationKey");
        private const string ApiHost = "api.elevenlabs.io";
        private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RateLimitTimeout = TimeSpan.FromSeconds(30);

        public ResilientElevenLabsClient(
            ElevenLabsClient client,
            ResiliencePipeline<ElevenLabs.VoiceClip> voiceClipPipeline,
            ResiliencePipeline<Voice> voicePipeline,
            ILogger<ResilientElevenLabsClient> logger,
            CombinedRateLimiter rateLimiter)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _voiceClipPipeline = voiceClipPipeline ?? throw new ArgumentNullException(nameof(voiceClipPipeline));
            _voicePipeline = voicePipeline ?? throw new ArgumentNullException(nameof(voicePipeline));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        }

        private async Task<bool> IsApiEndpointReachableAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient { Timeout = HealthCheckTimeout };
                var response = await client.GetAsync($"https://{ApiHost}/health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reach ElevenLabs API endpoint");
                return false;
            }
        }

        private async Task EnsureApiEndpointReachableAsync(CancellationToken cancellationToken)
        {
            if (!await IsApiEndpointReachableAsync(cancellationToken))
            {
                var message = $"Failed to reach ElevenLabs API endpoint ({ApiHost}). Please check your network connection and DNS settings.";
                _logger.LogError(message);
                throw new HttpRequestException(message);
            }
        }

        private async Task<T> ExecuteWithRateLimitAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string operationName,
            ResiliencePipeline<T> pipeline,
            CancellationToken cancellationToken)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            try
            {
                context.Properties.Set(OperationKey, operationName);
                return await pipeline.ExecuteAsync(async (ctx) =>
                {
                    try
                    {
                      //  await EnsureApiEndpointReachableAsync(cancellationToken);

                        // Acquire rate limit with timeout
                        var linkedToken = _rateLimiter.CreateLinkedTokenWithTimeout(cancellationToken, RateLimitTimeout);
                        await _rateLimiter.AcquireAsync(linkedToken);
                        try
                        {
                            return await operation(cancellationToken);
                        }
                        finally
                        {
                            _rateLimiter.Release();
                        }
                    }
                    catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx && 
                        (socketEx.SocketErrorCode == SocketError.HostNotFound || 
                         socketEx.SocketErrorCode == SocketError.NoData))
                    {
                        _logger.LogError(ex, "DNS resolution failed for ElevenLabs API. Please check your network connection and DNS settings.");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Operation {OperationName} failed", operationName);
                        throw;
                    }
                }, context);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }

        public async Task<ElevenLabs.VoiceClip> TextToSpeechAsync(TextToSpeechRequest request, string voiceId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrEmpty(voiceId);

            return await ExecuteWithRateLimitAsync(
                async (ct) => await _client.TextToSpeechEndpoint.TextToSpeechAsync(request, null, ct),
                $"TTS_{voiceId}",
                _voiceClipPipeline,
                cancellationToken);
        }

        public async Task<Voice> GetVoiceAsync(string voiceId, bool withSettings, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(voiceId);

            return await ExecuteWithRateLimitAsync(
                async (ct) => await _client.VoicesEndpoint.GetVoiceAsync(voiceId, withSettings, ct),
                $"GetVoice_{voiceId}",
                _voicePipeline,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            // Rate limiter is a singleton, don't dispose it here
            await ValueTask.CompletedTask;
        }
    }
} 