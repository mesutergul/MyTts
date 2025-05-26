using Google.Cloud.TextToSpeech.V1;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;

namespace MyTts.Services.Clients
{
    public class CloudTtsClient : ICloudTtsClient // Renamed class
    {
        private readonly ILogger<CloudTtsClient> _logger;
        private readonly CloudTtsConfig _config;
        private readonly TextToSpeechClient? _client;
        private readonly bool _isEnabled;

        public CloudTtsClient(IOptions<CloudTtsConfig> cloudTtsConfigOptions, ILogger<CloudTtsClient> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = cloudTtsConfigOptions?.Value ?? throw new ArgumentNullException(nameof(cloudTtsConfigOptions));

            if (!_config.Enabled)
            {
                _isEnabled = false;
                _client = null;
                _logger.LogInformation("Google Cloud TTS service is disabled");
                return;
            }

            try
            {
                _client = TextToSpeechClient.Create();
                _isEnabled = true;
                _logger.LogInformation("Google Cloud TTS service initialized successfully");
            }
            catch (Exception ex)
            {
                _isEnabled = false;
                _client = null;
                _logger.LogWarning(ex, "Failed to initialize Google Cloud TTS service. The service will be disabled.");
            }
        }

        public async Task<byte[]> SynthesizeSpeechAsync(string text, string languageCode, string voiceName, CancellationToken cancellationToken = default)
        {
            if (!_isEnabled || _client == null)
            {
                _logger.LogInformation("Google Cloud TTS service is not available");
                return Array.Empty<byte>();
            }

            try
            {
                var input = new SynthesisInput
                {
                    Text = text
                };

                var voice = new VoiceSelectionParams
                {
                    LanguageCode = languageCode,
                    Name = voiceName,
                    SsmlGender = SsmlVoiceGender.Neutral
                };

                var audioConfig = new AudioConfig
                {
                    AudioEncoding = AudioEncoding.Mp3,
                    SpeakingRate = _config.SpeakingRate,
                    Pitch = _config.Pitch,
                    VolumeGainDb = _config.VolumeGainDb
                };

                var response = await _client.SynthesizeSpeechAsync(input, voice, audioConfig, cancellationToken);
                return response.AudioContent.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to synthesize speech using Google Cloud TTS");
                return Array.Empty<byte>();
            }
        }
        public bool IsAvailable => _isEnabled && _client != null;
        public async ValueTask DisposeAsync()
        {
            await Task.Run(() => GC.SuppressFinalize(this));
        }
    }
}