namespace MyTts.Models
{
    public record AudioMetadata
    {
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Album { get; init; } = string.Empty;
        public int Year { get; init; }
        public string Language { get; init; } = string.Empty;
        public TimeSpan Duration { get; init; }
        public int BitRate { get; init; }
        public int SampleRate { get; init; }
        public int Channels { get; init; }
        public string Format { get; init; } = string.Empty;
        public string Codec { get; init; } = string.Empty;
    }
} 