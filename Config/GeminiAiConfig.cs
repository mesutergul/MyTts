namespace MyTts.Config
{
    public class GeminiAiConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        // Add any other relevant Gemini AI settings here
        // For example, model name, voice options, etc.
        public string ModelName { get; set; } = "gemini-pro"; // Or your desired default
    }
}
