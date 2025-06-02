using ElevenLabs;
using ElevenLabs.TextToSpeech;
using Microsoft.Extensions.Logging;
using Polly;
using MyTts.Helpers;

namespace MyTts.Services.Clients
{
    public class ResilientElevenLabsClient
    {
        private readonly ElevenLabsClient _client;
        private readonly ResiliencePipeline<VoiceClip> _pipeline;
        private readonly ILogger<ResilientElevenLabsClient> _logger;
        private readonly CombinedRateLimiter _rateLimiter;
        private static readonly ResiliencePropertyKey<string> OperationKey = new("OperationKey");

        public ResilientElevenLabsClient(
            ElevenLabsClient client,
            ResiliencePipeline<VoiceClip> pipeline,
            ILogger<ResilientElevenLabsClient> logger,
            CombinedRateLimiter rateLimiter)
        {
            _client = client;
            _pipeline = pipeline;
            _logger = logger;
            _rateLimiter = rateLimiter;
        }

        public async Task<VoiceClip> TextToSpeechAsync(TextToSpeechRequest request, string voiceId, CancellationToken cancellationToken = default)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            try
            {
                context.Properties.Set(OperationKey, $"TTS_{voiceId}");
                var result = await _pipeline.ExecuteAsync(async (ctx) =>
                {
                    try
                    {
                        // Acquire rate limit before making the API call
                        await _rateLimiter.AcquireAsync();
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
    }
} 