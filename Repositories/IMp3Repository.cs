
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;

namespace MyTts.Repositories
{
    public interface IMp3Repository : IDisposable
    {
        string GetFullPath(string filePath, AudioType fileType);
        Task<byte[]> LoadMp3FileAsync(int filePath, AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3FileAsync(int filePath, byte[] fileData, AudioType fileType, CancellationToken cancellationToken);
        Task<List<Mp3Meta>> LoadListMp3MetadatasAsync(AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3MetadatasAsync(List<Mp3Meta> mp3Files, AudioType fileType, CancellationToken cancellationToken);
        Task DeleteMp3FileAsync(string filePath, CancellationToken cancellationToken);
        Task<bool> Mp4FileExistsAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadMp3MetaByPathAsync(int filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadMp3MetaByNewsIdAsync(int id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadLatestMp3MetaByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task SaveSingleMp3MetaAsync(Mp3Meta mp3File, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta> LoadAndCacheMp3File(int id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Meta?> GetFromCacheAsync(string key, CancellationToken cancellationToken);
        Task<bool> FileExistsAnywhereAsync(int id, AudioType fileType, CancellationToken cancellationToken);
        Task SetToCacheAsync(string key, Mp3Meta value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<Stream> ReadLargeFileAsStreamAsync(int id, int bufferSize, AudioType fileType, bool isMerged, CancellationToken cancellationToken);
        Task SaveMp3MetaToSql(Mp3Meta mp3Meta, CancellationToken cancellationToken);
        Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken);
        Task<List<int>> GetExistingMetaList(List<int> values, CancellationToken cancellationToken);
        Task<News> LoadNewsAsync(int news, CancellationToken cancellationToken);
        Task<byte[]> ReadFileFromDiskAsync(int filePath, AudioType fileType = AudioType.Mp3, CancellationToken cancellationToken = default);
    }
}