using Google.Cloud.TextToSpeech.V1; // New using directive
using Google.Apis.Auth.OAuth2; // For GoogleCredential
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
namespace MyTts.Services.Clients
{
    public class CloudTtsClient : ICloudTtsClient // Renamed class
    {
        private readonly CloudTtsConfig _cloudTtsConfig;
        private readonly ILogger<CloudTtsClient> _logger;
        private readonly TextToSpeechClient _textToSpeechClient; // Use the official client

        public CloudTtsClient(
            IOptions<CloudTtsConfig> cloudTtsConfigOptions,
            ILogger<CloudTtsClient> logger)
        {
            _cloudTtsConfig = cloudTtsConfigOptions.Value ?? throw new ArgumentNullException(nameof(cloudTtsConfigOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(_cloudTtsConfig.ApiKey))
            {
                _logger.LogError("Google Cloud TTS API Key is not configured.");
                throw new InvalidOperationException("Google Cloud TTS API Key is missing.");
            }
            // Environment.SetEnvironmentVariable("GOOGLE_API_KEY", _cloudTtsConfig.ApiKey);
            _textToSpeechClient = TextToSpeechClient.Create();

            _logger.LogInformation("Google Cloud TTS Client initialized.");
        }

        public async Task<byte[]> SynthesizeSpeechAsync(
            string text,
            string languageCode,
            string? voiceName, // e.g., "tr-TR-Standard-A"
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Text to synthesize cannot be empty. Returning empty byte array.");
                return Array.Empty<byte>();
            }

            if (string.IsNullOrWhiteSpace(voiceName))
            {
                _logger.LogError("Voice name for Google Cloud TTS cannot be empty or null when using the client library. E.g., 'tr-TR-Standard-A'.");
                throw new ArgumentException("A valid voice name is required for Google Cloud TTS.");
            }

            _logger.LogInformation(
                "Attempting to synthesize speech with Google Cloud TTS. Text length: {TextLength}, Language: {Language}, Voice: {Voice}",
                text.Length, languageCode, voiceName);

            try
            {
                var synthesisInput = new SynthesisInput { Text = text };
                var voiceSelectionParams = new VoiceSelectionParams
                {
                    LanguageCode = languageCode,
                    Name = voiceName
                };
                var audioConfig = new AudioConfig
                {
                    AudioEncoding = AudioEncoding.Mp3 // Enum from the library
                };

                // Perform the synthesis using the client library
                var response = await _textToSpeechClient.SynthesizeSpeechAsync(synthesisInput, voiceSelectionParams, audioConfig, cancellationToken: cancellationToken);

                if (response?.AudioContent != null && response.AudioContent.Length > 0)
                {
                    var audioBytes = response.AudioContent.ToByteArray();
                    _logger.LogInformation("Successfully received audio stream from Google Cloud TTS. Byte length: {Length}", audioBytes.Length);
                    return audioBytes;
                }
                else
                {
                    _logger.LogError("Google Cloud TTS returned empty or null audio content.");
                    throw new Exception("Google Cloud TTS returned empty or null audio content.");
                }
            }
            catch (Google.GoogleApiException gapiEx)
            {
                _logger.LogError(gapiEx, "Google Cloud TTS API call failed. Status: {Status}, Message: {Message}", gapiEx.HttpStatusCode, gapiEx.Message);
                throw new Exception($"Google Cloud TTS API call failed. Status: {gapiEx.HttpStatusCode}, Message: {gapiEx.Message}", gapiEx);
            }
            catch (OperationCanceledException opEx)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(opEx, "Google Cloud TTS request was canceled by the caller.");
                }
                else
                {
                    _logger.LogWarning(opEx, "Google Cloud TTS request timed out or was canceled internally.");
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during Google Cloud TTS synthesis.");
                throw new Exception("An unexpected error occurred during Google Cloud TTS synthesis.", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Run(() => GC.SuppressFinalize(this));
        }
    }
}