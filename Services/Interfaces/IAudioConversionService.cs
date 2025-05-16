namespace MyTts.Services.Interfaces
{
    public interface IAudioConversionService
    {
        Task<string> ConvertMp3ToM4aAsync(byte[] mp3Data, CancellationToken cancellationToken);
    }
} 