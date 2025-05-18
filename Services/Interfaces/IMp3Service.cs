using Microsoft.AspNetCore.Mvc;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;
using MyTts.Repositories;
namespace MyTts.Services.Interfaces
{
    public interface IMp3Service
    {
        Task<IEnumerable<Mp3Dto>> GetFeedByLanguageAsync(ListRequest request, CancellationToken cancellationToken);
        Task<IEnumerable<Mp3Dto>> GetMp3FileListAsync(string language,AudioType fileType, CancellationToken cancellationToken);
        Task<Stream> CreateSingleMp3Async(OneRequest request, AudioType fileType, CancellationToken cancellationToken);
        Task<(Stream audioData, string contentType, string fileName)> CreateMultipleMp3Async(string language, int limit, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto> GetMp3FileAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto> GetLastMp3ByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task<IActionResult> DownloadMp3(int id, string language, AudioType fileType, CancellationToken cancellationToken);
        Task<IActionResult> StreamMp3(int id, string language, AudioType fileType, CancellationToken cancellationToken);
        Task<IEnumerable<Mp3Dto>> GetMp3FileListByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task<Stream> GetAudioFileStream(int id, string language, AudioType fileType, bool isMerged, CancellationToken cancellationToken);
        Task<bool> FileExistsAnywhereAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken);
        Task<byte[]> GetMp3FileBytes(int fileName, string language, AudioType fileType, CancellationToken cancellationToken);
        Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken);
    }
}