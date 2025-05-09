namespace MyTts.Services
{
    using System.Diagnostics;

    public interface IAudioConversionService
    {
        Task<string> ConvertMp3ToM4aAsync(byte[] mp3Data, CancellationToken cancellationToken);
    }

    public class AudioConversionService : IAudioConversionService
    {
        private readonly ILogger<AudioConversionService> _logger;

        public AudioConversionService(ILogger<AudioConversionService> logger)
        {
            _logger = logger;
        }

        public async Task<string> ConvertMp3ToM4aAsync(byte[] mp3Data, CancellationToken cancellationToken)
        {
            var tempMp3 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
            var tempM4a = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".m4a");

            await File.WriteAllBytesAsync(tempMp3, mp3Data, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{tempMp3}\" -c:a aac -b:a 128k \"{tempM4a}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg conversion failed: {Error}", error);
                throw new InvalidOperationException($"FFmpeg failed: {error}");
            }

            File.Delete(tempMp3); // Clean up input

            return tempM4a;
        }
    }

}
