
using Microsoft.AspNetCore.Mvc;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;
using MyTts.Repositories;
namespace MyTts.Services
{
    public interface IMp3Service
    {
        Task<IEnumerable<Mp3Meta>> GetFeedByLanguageAsync(ListRequest request, CancellationToken cancellationToken);
        Task<IEnumerable<Mp3Meta>> GetMp3FileListAsync(AudioType fileType, CancellationToken cancellationToken);
        Task<Stream> CreateSingleMp3Async(OneRequest request, AudioType fileType, CancellationToken cancellationToken);
        Task<(Stream audioData, string contentType, string fileName)> CreateMultipleMp3Async(string language, int limit, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> GetMp3FileAsync(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> GetLastMp3ByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task<IActionResult> DownloadMp3(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<IActionResult> StreamMp3(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<IEnumerable<Mp3Meta>> GetMp3FileListByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task<Stream> GetAudioFileStream(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<bool> FileExistsAnywhereAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<byte[]> GetMp3FileBytes(string fileName, AudioType fileType, CancellationToken cancellationToken);
    }
}