
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;

namespace MyTts.Repositories
{
    public interface IMp3Repository : IDisposable
    {
        Task<byte[]> LoadMp3FileAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3FileAsync(string filePath, byte[] fileData, AudioType fileType, CancellationToken cancellationToken);
        Task<List<Mp3Meta>> LoadListMp3MetadatasAsync(AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3MetadatasAsync(List<Mp3Meta> mp3Files, AudioType fileType, CancellationToken cancellationToken);
        Task DeleteMp3FileAsync(string filePath, CancellationToken cancellationToken);
        Task<bool> Mp4FileExistsAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadMp3MetaByPathAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadMp3MetaByNewsIdAsync(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadLatestMp3MetaByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task SaveSingleMp3MetaAsync(Mp3Meta mp3File, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadAndCacheMp3File(string id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta?> GetFromCacheAsync(string key, CancellationToken cancellationToken);
        Task<bool> FileExistsAnywhereAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task SetToCacheAsync(string key, Mp3Meta value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<Stream> ReadLargeFileAsStreamAsync(string fullPath, int bufferSize, AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3MetaToSql(Mp3Meta mp3Meta, CancellationToken cancellationToken);
        Task<List<HaberSummaryDto>> MyTestQuery(CancellationToken cancellationToken);
    }
}