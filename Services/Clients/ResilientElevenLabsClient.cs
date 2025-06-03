using ElevenLabs;
using ElevenLabs.TextToSpeech;
using Polly;
using MyTts.Helpers;
using ElevenLabs.Voices;
using System.Net.Sockets;
using System.Net;

namespace MyTts.Services.Clients
{
    public class ResilientElevenLabsClient : IAsyncDisposable
    {
        private readonly ElevenLabsClient _client;
        private readonly ResiliencePipeline<VoiceClip> _voiceClipPipeline;
        private readonly ResiliencePipeline<Voice> _voicePipeline;
        private readonly ILogger<ResilientElevenLabsClient> _logger;
        private readonly CombinedRateLimiter _rateLimiter;
        private static readonly ResiliencePropertyKey<string> OperationKey = new("OperationKey");
        private const string ApiHost = "api.elevenlabs.io";

        public ResilientElevenLabsClient(
            ElevenLabsClient client,
            ResiliencePipeline<VoiceClip> voiceClipPipeline,
            ResiliencePipeline<Voice> voicePipeline,
            ILogger<ResilientElevenLabsClient> logger,
            CombinedRateLimiter rateLimiter)
        {
            _client = client;
            _voiceClipPipeline = voiceClipPipeline;
            _voicePipeline = voicePipeline;
            _logger = logger;
            _rateLimiter = rateLimiter;
        }

        private async Task<bool> IsApiEndpointReachableAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
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
                _logger.LogError("ElevenLabs API endpoint is not reachable. Please check your network connection and DNS settings.");
                throw new HttpRequestException($"Failed to reach ElevenLabs API endpoint ({ApiHost}). Please check your network connection and DNS settings.");
            }
        }

        public async Task<VoiceClip> TextToSpeechAsync(TextToSpeechRequest request, string voiceId, CancellationToken cancellationToken = default)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            try
            {
                context.Properties.Set(OperationKey, $"TTS_{voiceId}");
                var result = await _voiceClipPipeline.ExecuteAsync(async (ctx) =>
                {
                    try
                    {
                        await EnsureApiEndpointReachableAsync(cancellationToken);

                        // Acquire rate limit before making the API call
                        var linkedToken = _rateLimiter.CreateLinkedTokenWithTimeout(cancellationToken);
                        await _rateLimiter.AcquireAsync(linkedToken);
                        try
                        {
                            var response = await _client.TextToSpeechEndpoint.TextToSpeechAsync(request, null, cancellationToken);
                            return (VoiceClip)response;
                        }
                        finally
                        {
                            // Always release the rate limiter, even if the API call fails
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
                        _logger.LogError(ex, "Failed to convert text to speech for voice {VoiceId}", voiceId);
                        throw;
                    }
                }, context);

                return result;
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }

        public async Task<Voice> GetVoiceAsync(string voiceId, bool withSettings, CancellationToken cancellationToken)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            try
            {
                context.Properties.Set(OperationKey, $"GetVoice_{voiceId}");
                var result = await _voicePipeline.ExecuteAsync(async (ctx) =>
                {
                    try
                    {
                        await EnsureApiEndpointReachableAsync(cancellationToken);

                        // Acquire rate limit before making the API call
                        var linkedToken = _rateLimiter.CreateLinkedTokenWithTimeout(cancellationToken);
                        await _rateLimiter.AcquireAsync(linkedToken);
                        try
                        {
                            var response = await _client.VoicesEndpoint.GetVoiceAsync(voiceId, withSettings, cancellationToken);
                            return response;
                        }
                        finally
                        {
                            // Always release the rate limiter, even if the API call fails
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
                        _logger.LogError(ex, "Failed to get voice {VoiceId}", voiceId);
                        throw;
                    }
                }, context);

                return result;
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Rate limiter is a singleton, don't dispose it here
        }
    }
} 