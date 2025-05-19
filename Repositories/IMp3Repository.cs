using MyTts.Data.Entities;
using MyTts.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyTts.Repositories
{
    public interface IMp3Repository : IDisposable
    {
        Task<byte[]> LoadMp3FileAsync(int filePath, string language, AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3FileAsync(int filePath, byte[] fileData, AudioType fileType, CancellationToken cancellationToken);
        Task<List<Mp3Dto>> LoadListMp3MetadatasAsync(AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3MetadataToSqlBatchAsync(List<Mp3Dto> mp3Files, AudioType fileType, CancellationToken cancellationToken);
        Task SaveMp3MetadataToJsonAndCacheAsync(List<Mp3Dto> mp3Files, AudioType fileType, CancellationToken cancellationToken);
        Task DeleteMp3FileAsync(string filePath, CancellationToken cancellationToken);
        Task<bool> Mp3FileExistsAsync(int id, AudioType fileType, CancellationToken cancellationToken);
        Task<bool> Mp3FileExistsInSqlAsync(int id, CancellationToken cancellationToken);
        Task<Mp3Dto> LoadMp3MetaByPathAsync(string filePath, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto> LoadMp3MetaByNewsIdAsync(int id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto> LoadLatestMp3MetaByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto> LoadAndCacheMp3File(int id, AudioType fileType, CancellationToken cancellationToken);
        Task<Mp3Dto?> GetFromCacheAsync(string key, CancellationToken cancellationToken);
        Task<bool> FileExistsAnywhereAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken);
        Task SetToCacheAsync(string key, Mp3Dto value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<Stream> ReadLargeFileAsStreamAsync(int id, string language, int bufferSize, AudioType fileType, bool isMerged, CancellationToken cancellationToken);
        Task SaveMp3MetaToSql(Mp3Dto mp3Dto, CancellationToken cancellationToken);
        Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken);
        Task<List<int>> GetExistingMetaList(List<int> values, CancellationToken cancellationToken);
        Task<NewsDto> LoadNewsAsync(int news, CancellationToken cancellationToken);
        Task<byte[]> ReadFileFromDiskAsync(int filePath, AudioType fileType = AudioType.Mp3, CancellationToken cancellationToken = default);
    }
}