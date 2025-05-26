namespace MyTts.Config
{
    public class CloudTtsConfig
    {
        public bool Enabled { get; set; }
        public string? CredentialsPath { get; set; }
        public double SpeakingRate { get; set; } = 1.0;
        public double Pitch { get; set; } = 0.0;
        public double VolumeGainDb { get; set; } = 0.0;
        public string ApiKey { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        // Add any other relevant Gemini AI settings here
        // For example, model name, voice options, etc.
        public string ModelName { get; set; } = "gemini-pro"; // Or your desired default
    }
}