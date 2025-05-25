using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Grpc.Auth;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Models; // For AudioType, if needed, or other shared models
using MyTts.Services.Interfaces;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyTts.Services.Clients
{
    public class GeminiTtsClient : IGeminiTtsClient
    {
        private readonly GeminiAiConfig _geminiConfig;
        private readonly ILogger<GeminiTtsClient> _logger;
        private readonly HttpClient _httpClient; // For REST API calls

        // If using Google Cloud SDK for Text-to-Speech (more involved setup)
        // private readonly TextToSpeechClient _textToSpeechClient; 

        public GeminiTtsClient(
            IOptions<GeminiAiConfig> geminiConfigOptions,
            ILogger<GeminiTtsClient> logger,
            IHttpClientFactory httpClientFactory)
        {
            _geminiConfig = geminiConfigOptions.Value ?? throw new ArgumentNullException(nameof(geminiConfigOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClientFactory.CreateClient("GeminiTtsClient"); // Configure this client in HttpClientsConfig.cs

            if (string.IsNullOrWhiteSpace(_geminiConfig.ApiKey))
            {
                _logger.LogError("Gemini AI API Key is not configured.");
                throw new InvalidOperationException("Gemini AI API Key is missing.");
            }
            if (string.IsNullOrWhiteSpace(_geminiConfig.Endpoint))
            {
                 _logger.LogError("Gemini AI Endpoint is not configured.");
                throw new InvalidOperationException("Gemini AI Endpoint is missing.");
            }
             _httpClient.BaseAddress = new Uri(_geminiConfig.Endpoint);
             _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", _geminiConfig.ApiKey);
        }

        public async Task<Stream> SynthesizeSpeechAsync(
            string text,
            string languageCode, // e.g., "en-US"
            string? voiceName,    // Model dependent, e.g., "text-to-speech-1" or specific voice
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Text to synthesize cannot be empty. Returning Stream.Null.");
                return Stream.Null;
            }

            _logger.LogInformation(
                "Attempting to synthesize speech with Gemini AI. Text length: {TextLength}, Language: {Language}, Model/Voice: {Voice}",
                text.Length, languageCode, voiceName ?? _geminiConfig.ModelName);

            // NOTE: The following is a conceptual representation of calling a Gemini Text-to-Speech API.
            // The actual API endpoint, request body, and response handling will depend on 
            // the specific Gemini Text-to-Speech API Google provides.
            // This will likely be a REST API call.

            // Example structure for a REST API call:
            // Refer to the official Google Gemini Text-to-Speech documentation for the correct API details.
            var requestUri = $"v1beta/text:synthesize"; // This is a placeholder URI part

            var payload = new
            {
                input = new { text = text },
                voice = new { languageCode = languageCode, name = voiceName ?? _geminiConfig.ModelName }, // Adjust based on API
                audioConfig = new { audioEncoding = "MP3" } // Or "LINEAR16" etc.
            };

            string jsonPayload = "{}"; // Default to empty JSON if serialization fails
            try
            {
                jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                _logger.LogDebug("Gemini TTS Request Payload: {Payload}", jsonPayload);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "HTTP request to Gemini AI failed before receiving a response. Payload: {Payload}", jsonPayload);
                    throw new HttpRequestException($"HTTP request to Gemini AI failed. Payload: {jsonPayload}", httpEx);
                }
                catch (OperationCanceledException opEx)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation(opEx, "Gemini AI TTS request was canceled by the caller. Payload: {Payload}", jsonPayload);
                    }
                    else
                    {
                        _logger.LogWarning(opEx, "Gemini AI TTS request timed out or was canceled internally by HttpClient. Payload: {Payload}", jsonPayload);
                    }
                    throw; // Re-throw OperationCanceledException
                }


                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("Gemini TTS Raw Success Response: {ResponseBody}", responseBody);

                    try
                    {
                        var jsonResponse = JsonDocument.Parse(responseBody);
                        if (jsonResponse.RootElement.TryGetProperty("audioContent", out var audioContentElement))
                        {
                            var base64Audio = audioContentElement.GetString();
                            if (!string.IsNullOrEmpty(base64Audio))
                            {
                                var audioBytes = Convert.FromBase64String(base64Audio);
                                _logger.LogInformation("Successfully received and decoded audio stream from Gemini AI. Byte length: {Length}", audioBytes.Length);
                                return new MemoryStream(audioBytes);
                            }
                            else
                            {
                                _logger.LogError("Gemini AI TTS response contained an empty 'audioContent' field. Response: {ResponseBody}", responseBody);
                                throw new Exception($"Gemini AI TTS returned empty audio content. Response: {responseBody}");
                            }
                        }
                        else
                        {
                            _logger.LogError("Gemini AI TTS response did not contain 'audioContent' field. Response: {ResponseBody}", responseBody);
                            throw new Exception($"Gemini AI TTS response missing 'audioContent' field. Response: {responseBody}");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Error parsing successful Gemini AI TTS JSON response. ResponseBody: {ResponseBody}", responseBody);
                        throw new Exception($"Error parsing successful Gemini AI TTS JSON response. ResponseBody: {responseBody}", jsonEx);
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Gemini AI TTS request failed with status code {StatusCode}. Response: {ErrorBody}. Request Payload: {Payload}", response.StatusCode, errorBody, jsonPayload);
                    throw new HttpRequestException($"Gemini AI TTS request failed. Status: {response.StatusCode}, Response: {errorBody}, Payload: {jsonPayload}");
                }
            }
            catch (HttpRequestException) // Re-throw if already specific
            {
                throw;
            }
            catch (OperationCanceledException) // Re-throw if already specific
            {
                 throw;
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                _logger.LogError(ex, "An unexpected error occurred in GeminiTtsClient.SynthesizeSpeechAsync. Request Payload: {Payload}", jsonPayload);
                // Wrap in a general exception to avoid exposing too many details or if it's not an HTTP error
                throw new Exception($"An unexpected error occurred during Gemini TTS synthesis. Payload: {jsonPayload}", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _httpClient?.Dispose();
            // If using Google Cloud SDK client:
            // if (_textToSpeechClient != null) { await _textToSpeechClient.ShutdownAsync(); }
            GC.SuppressFinalize(this);
        }
    }
}
