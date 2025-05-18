using System.Collections.Concurrent;
using Newtonsoft.Json;
using MyTts.Data.Interfaces;
using System.Text;
using MyTts.Data.Entities;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Services.Constants;
using MyTts.Services;

namespace MyTts.Repositories
{
    public class Mp3Repository : IMp3Repository
    {
        private readonly ILogger<Mp3Repository> _logger;
        private readonly string _baseStoragePath;
        private readonly string _metadataPath;
        private readonly IRedisCacheService _cache;
        private readonly SemaphoreSlim _dbLock;
        private readonly JsonSerializerSettings _jsonSettings;
        private bool _disposed;
        private readonly IMp3MetaRepository _mp3MetaRepository;
        private readonly INewsRepository _newsRepository;
        private readonly ILocalStorageService _localStorageService;

        private static readonly string STORAGE_PREFIX_KEY = "speech_";
        private static readonly TimeSpan DB_CACHE_DURATION = RedisKeys.DB_CACHE_DURATION;
        private static readonly TimeSpan FILE_CACHE_DURATION = RedisKeys.FILE_CACHE_DURATION;

        public static string GetStorageKey(int id) => $"{STORAGE_PREFIX_KEY}{id}";
        public static string GetCacheKey(int id) => RedisKeys.FormatKey(RedisKeys.MP3_FILE_KEY, id);

        public Mp3Repository(
            ILogger<Mp3Repository> logger,
            IConfiguration configuration,
            ILocalStorageService localStorageService,
            IRedisCacheService cache,
            IMp3MetaRepository mp3MetaRepository,
            INewsRepository newsRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _baseStoragePath = Path.GetFullPath(configuration["Storage:BasePath"]) ?? "C:\\repos\\audio";
            _metadataPath = Path.GetFullPath(configuration["Storage:MetadataPath"]) ?? "C:\\repos\\audiometa\\mp3files.json";
            _dbLock = new SemaphoreSlim(1, 1);
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            _localStorageService = localStorageService ?? throw new ArgumentNullException(nameof(localStorageService));
            _mp3MetaRepository = mp3MetaRepository ?? throw new ArgumentNullException(nameof(mp3MetaRepository));
            _newsRepository = newsRepository ?? throw new ArgumentNullException(nameof(newsRepository));
            // Initialize directories asynchronously
            InitializeDirectoriesAsync().GetAwaiter().GetResult();
        }

        public async Task<bool> Mp3FileExistsInCacheAsync(string cacheKey, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(cacheKey);
            if (_cache == null) return false;
            try
            {
                var existsInCache = await _cache.GetAsync<bool?>(cacheKey);
                if (existsInCache.HasValue)
                {
                    _logger.LogDebug("Cache hit: Found existence flag for key {CacheKey}", cacheKey);
                }
                else
                {
                    _logger.LogDebug("Cache miss: No existence flag found for key {CacheKey}", cacheKey);
                }
                return existsInCache.HasValue ? existsInCache.Value : false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis error checking existence flag for key {CacheKey}", cacheKey);
                throw;
            }
        }
        public async Task<bool> Mp3FileExistsInSqlAsync(int id, CancellationToken cancellationToken)
        {
            if (_mp3MetaRepository == null)
            {
                _logger.LogWarning("SQL repository not registered. Skipping DB check.");
                return false;
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
            try
            {
                await _dbLock.WaitAsync();
                try
                {
                    var exist = await _mp3MetaRepository.ExistByIdAsync(id, cancellationToken);
                    return exist;
                }
                finally
                {
                    _dbLock.Release();
                }
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                _logger.LogError(ex, "Failed to load MP3 metadata for path: {id}", id);
                return false;
            }
        }
        public async Task<bool> FileExistsAnywhereAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            string cacheKey = GetCacheKey(id);
            bool existsInDisk = await Mp3FileExistsAsync(id, fileType, cancellationToken);
            if (existsInDisk)
            {
                if (!await Mp3FileExistsInCacheAsync(cacheKey, cancellationToken))
                {
                    await _cache.SetAsync(cacheKey, true, FILE_CACHE_DURATION);
                }
                if (!await Mp3FileExistsInSqlAsync(id, cancellationToken))
                {
                    await SaveMp3MetaToSql(new Mp3Dto { FileId = id, FileUrl = GetFullPath(GetStorageKey(id), fileType), Language = language }, cancellationToken);
                }
            } else _logger.LogInformation("File not found in cache, database, or disk for ID {Id}", id);
            return existsInDisk;
        }
        public async Task<bool> Mp3FileExistsAsync(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            string storageKey = GetStorageKey(id);
            string fullPath = GetFullPath(storageKey, fileType);
            _logger.LogInformation("Checking if file exists at {FullPath}", fullPath);
            return await _localStorageService.FileExistsAsync(fullPath);
        }

        public async Task<byte[]> LoadMp3FileAsync(int filePath, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                if (await FileExistsAnywhereAsync(filePath, language, fileType, cancellationToken))
                {
                    return await ReadFileFromDiskAsync(filePath, fileType, cancellationToken);
                }
                return Array.Empty<byte>();
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "File not found: {FilePath}", filePath);
                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load MP3 file: {FilePath}", filePath);
                throw;
            }
        }
        public async Task<byte[]> ReadFileFromDiskAsync(int filePath, AudioType fileType = AudioType.Mp3, CancellationToken cancellationToken = default)
        {
            string storageKey = GetStorageKey(filePath);
            string fullPath = GetFullPath(storageKey, fileType);
            try
            {
                if (!await _localStorageService.FileExistsAsync(fullPath))
                {
                    _logger.LogWarning("File not found or empty at path: {Path}", fullPath);
                    return Array.Empty<byte>();
                }
                var fileInfo = new FileInfo(fullPath);
                // Choose reading strategy based on file size
                return fileInfo.Length > 100 * 1024 * 1024 // 100MB threshold
                    ? await _localStorageService.ReadLargeFileAsync(fullPath, cancellationToken)
                    : await File.ReadAllBytesAsync(fullPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file from disk: {Path}", fullPath);
                throw new IOException($"Failed to read file: {fullPath}", ex);
            }
        }
        public async Task<Stream> ReadLargeFileAsStreamAsync(int id, string language, int bufferSize, AudioType fileType, bool isMerged, CancellationToken cancellationToken)
        {
            string storageKey = isMerged ? "merged" : GetStorageKey(id);
            string fullPath = GetFullPath(storageKey, fileType);

            if (!await _localStorageService.FileExistsAsync(fullPath))
            {
                _logger.LogError("File not found: " + fullPath);
                throw new FileNotFoundException("Cannot open file: " + fullPath);
            }

            return await _localStorageService.ReadLargeFileAsStreamAsync(fullPath, cancellationToken);
        }
        public async Task SaveMp3FileAsync(int filePath, byte[] fileData, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fileData);

            string storageKey = GetStorageKey(filePath);
            string cacheKey = GetCacheKey(filePath);
            string fullPath = GetFullPath(storageKey, fileType);

            try
            {
                string directory = Path.GetDirectoryName(fullPath)!;
                if (!await _localStorageService.DirectoryExistsAsync(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Create a VoiceClip from the file data
                using var voiceClip = new VoiceClip(fileData);
                await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await voiceClip.CopyToAsync(fileStream, 128 * 1024, cancellationToken);

                // Update cache with consistent key
                _logger.LogDebug("Caching file data for ID {Id} with key {CacheKey}, size: {Size} bytes",
                    filePath, cacheKey, fileData.Length);
                await _cache.SetAsync(cacheKey, fileData, FILE_CACHE_DURATION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save and cache file for ID {Id}", filePath);
                throw;
            }
        }
        public async Task DeleteMp3FileAsync(string filePath, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            try
            {
                string storageKey = GetStorageKey(int.Parse(filePath));
                string cacheKey = GetCacheKey(int.Parse(filePath));
                string fullPath = GetFullPath(storageKey, AudioType.Mp3);

                await _localStorageService.DeleteFileAsync(fullPath);
                await _cache.RemoveAsync(cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file and remove cache for ID {Id}", filePath);
                throw;
            }
        }

        private async Task InitializeDirectoriesAsync()
        {
            try
            {
                // Create base storage directory
                if (!await _localStorageService.DirectoryExistsAsync(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                    _logger.LogInformation("Created base storage directory: {Path}", _baseStoragePath);
                }

                // Create database directory
                string dbDirectory = Path.GetDirectoryName(_metadataPath)!;
                if (!await _localStorageService.DirectoryExistsAsync(dbDirectory))
                {
                    Directory.CreateDirectory(dbDirectory);
                    _logger.LogInformation("Created database directory: {Path}", dbDirectory);
                }

                // Verify write permissions
                await VerifyDirectoryAccessAsync(_baseStoragePath);
                await VerifyDirectoryAccessAsync(dbDirectory);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied while creating directories. Please check permissions");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error while creating directories");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize directories");
                throw;
            }
        }

        public async Task VerifyDirectoryAccessAsync(string path)
        {
            string tempFilePath = Path.Combine(path, $".write-test-{Guid.NewGuid()}.tmp");

            try
            {
                // Create a test VoiceClip
                using var voiceClip = new VoiceClip(Encoding.UTF8.GetBytes("test"));
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await voiceClip.CopyToAsync(fileStream, 128 * 1024, CancellationToken.None);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Permissions error: Cannot write to directory '{Path}'. Please check file system permissions for the application user.", path);
                throw new InvalidOperationException($"The application does not have write permissions for the directory: {path}. Please ensure the user running the application has write access.", ex);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "An I/O error occurred while verifying directory access for '{Path}'.", path);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while verifying directory access for '{Path}'.", path);
                throw;
            }
            finally
            {
                if (await _localStorageService.FileExistsAsync(tempFilePath))
                {
                    await _localStorageService.DeleteFileAsync(tempFilePath);
                }
            }
        }

        #region File Operations

        public string GetFullPath(string filePath, AudioType fileType = AudioType.Mp3)
        {
            return Path.Combine(_baseStoragePath, filePath + "." + fileType.ToString().ToLower());
        }
        public async Task<Mp3Dto?> LoadMp3MetaByPathAsync(int filePath, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                var mp3Files = await LoadListMp3MetadatasAsync(fileType, cancellationToken);
                return mp3Files.FirstOrDefault(f => f.FileUrl == filePath.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load single MP3 file: {FilePath}", filePath);
                throw;
            }
        }

        #endregion

        #region Database Operations

        public async Task SaveMp3MetaToSql(Mp3Dto mp3Dto, CancellationToken cancellationToken)
        {
            try
            {
                await _dbLock.WaitAsync();
                try
                {
                    var exist = await _mp3MetaRepository.ExistByIdAsync(mp3Dto.FileId, cancellationToken);
                    if (!exist)
                    {
                        await _mp3MetaRepository.AddAsync(mp3Dto, cancellationToken).ConfigureAwait(false);
                        _logger.LogDebug("Saved MP3 metadata to SQL: {Mp3Meta}", mp3Dto);
                    }
                    else _logger.LogDebug("MP3 metadata already exists in SQL: {Mp3Meta}", mp3Dto);
                }
                finally
                {
                    _dbLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save MP3 metadata to SQL :{dto}", mp3Dto);
                throw;
            }
        }
        public async Task<Mp3Dto> LoadMp3MetaByNewsIdAsync(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                await _dbLock.WaitAsync();
                try
                {
                    var mp3File = await _mp3MetaRepository.GetByIdAsync(id, cancellationToken)
                        ?? throw new KeyNotFoundException($"MP3 file not found for ID: {id}");

                    _logger.LogDebug("Found MP3 metadata for ID: {Id}", id);

                    return mp3File;
                }
                finally
                {
                    _dbLock.Release();
                }
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                _logger.LogError(ex, "Failed to load MP3 metadata for ID: {Id}", id);
                throw;
            }
        }

        public async Task<Mp3Dto> LoadLatestMp3MetaByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                var mp3Files = await LoadListMp3MetadatasAsync(fileType, cancellationToken);
                return mp3Files
                    .Where(f => f.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.CreatedDate)
                    .FirstOrDefault()
                    ?? throw new KeyNotFoundException($"No MP3 file found for language: {language}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get latest MP3 file for language: {Language}", language);
                throw;
            }
        }

        public async Task SaveSingleMp3MetaAsync(Mp3Dto mp3File, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                var mp3Files = await LoadListMp3MetadatasAsync(fileType, cancellationToken);
                mp3Files.Add(mp3File);
                await SaveMp3MetadatasAsync(mp3Files, fileType, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save MP3 file metadata");
                throw;
            }
        }
        public async Task<Mp3Dto?> LoadAndCacheMp3File(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            var mp3File = await LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken);
            if (mp3File != null)
            {
                string cacheKey = GetCacheKey(id);
                _logger.LogDebug("Caching metadata for ID {Id} with key {CacheKey} for {Duration} hours",
                    id, cacheKey, RedisKeys.DEFAULT_METADATA_EXPIRY.TotalHours);
                await SetToCacheAsync(cacheKey, mp3File, RedisKeys.DEFAULT_METADATA_EXPIRY, cancellationToken);
            }
            else
            {
                _logger.LogInformation("No metadata found to cache for ID {Id}", id);
            }
            return mp3File;
        }
        public async Task<Mp3Dto?> GetFromCacheAsync(string key, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(_cache);
            var result = await _cache.GetAsync<Mp3Dto>(key, cancellationToken);
            if (result != null)
            {
                _logger.LogDebug("Cache hit: Retrieved metadata for key {CacheKey}", key);
            }
            else
            {
                _logger.LogDebug("Cache miss: No metadata found for key {CacheKey}", key);
            }
            return result;
        }
        public async Task SetToCacheAsync(string key, Mp3Dto value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(_cache);
            _logger.LogDebug("Caching metadata with key {CacheKey}, expiry: {Expiry}",
                key, expiry?.ToString() ?? "default");
            await _cache.SetAsync(key, value, expiry, cancellationToken);
        }
        public async Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken)
        {
            try
            {
                await _dbLock.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogDebug("Loading MP3 files from database");
                    return await _newsRepository.getSummary(20, Models.MansetType.ana_manset, cancellationToken);
                }
                finally
                {
                    _dbLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute test query");
                throw;
            }
        }
        public async Task<NewsDto> LoadNewsAsync(int news, CancellationToken cancellationToken)
        {
               try
               {
                   await _dbLock.WaitAsync(cancellationToken);
                   try     
                   {
                       _logger.LogDebug("Loading MP3 files from database");
                       return await _newsRepository.GetByIdAsync(news, cancellationToken);
                   }
                   finally
                   {
                       _dbLock.Release();
                   }
               }  
               catch (Exception ex)
               {
                   _logger.LogError(ex, "Failed to execute test query");
                   throw;
               }
        }
        public async Task<List<int>> GetExistingMetaList(List<int> myList, CancellationToken cancellationToken)
        {
            try
            {
                await _dbLock.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogDebug("Loading MP3 files from database");
                    return await _mp3MetaRepository.GetExistingFileIdsInLast500Async(myList, cancellationToken);
                }
                finally
                {
                    _dbLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute test query");
                throw;
            }
        }

        public async Task<List<Mp3Dto>> LoadListMp3MetadatasAsync(AudioType fileType, CancellationToken cancellationToken)
        {
            // Try get from cache first using the standardized key
            var cachedData = await _cache.GetAsync<List<Mp3Dto>>(RedisKeys.MP3_METADATA_DB_KEY, cancellationToken);
            if (cachedData != null)
            {
                _logger.LogDebug("Cache hit: Retrieved metadata list with {Count} items", cachedData.Count);
                return cachedData;
            }

            await _dbLock.WaitAsync();
            try
            {
                // Double-check cache after acquiring lock
                cachedData = await _cache.GetAsync<List<Mp3Dto>>(RedisKeys.MP3_METADATA_DB_KEY, cancellationToken);
                if (cachedData != null)
                {
                    _logger.LogDebug("Cache hit after lock: Retrieved metadata list with {Count} items", cachedData.Count);
                    return cachedData;
                }

                _logger.LogDebug("Cache miss: Loading metadata list from file system");

                // Load from file if not in cache
                if (!await _localStorageService.FileExistsAsync(_metadataPath))
                {
                    var emptyList = new List<Mp3Dto>();
                    _logger.LogInformation("Caching empty metadata list as file does not exist");
                    await _cache.SetAsync(RedisKeys.MP3_METADATA_DB_KEY, emptyList, DB_CACHE_DURATION);
                    return emptyList;
                }

                // Read the file using a stream
                using var fileStream = await _localStorageService.ReadLargeFileAsStreamAsync(_metadataPath, cancellationToken);
                using var reader = new StreamReader(fileStream);
                var json = await reader.ReadToEndAsync();
                var mp3Files = JsonConvert.DeserializeObject<List<Mp3Dto>>(json, _jsonSettings)
                    ?? new List<Mp3Dto>();

                // Cache the loaded data
                _logger.LogInformation("Caching metadata list with {Count} items for {Duration} minutes",
                    mp3Files.Count, DB_CACHE_DURATION.TotalMinutes);
                await _cache.SetAsync(RedisKeys.MP3_METADATA_DB_KEY, mp3Files, DB_CACHE_DURATION);

                return mp3Files;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load or cache metadata list");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task SaveMp3MetadatasAsync(List<Mp3Dto> mp3Files, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(mp3Files);

            await _dbLock.WaitAsync();
            try
            {
                var json = JsonConvert.SerializeObject(mp3Files, Formatting.Indented);
                var jsonBytes = Encoding.UTF8.GetBytes(json);

                // Create a VoiceClip from the JSON data
                using var voiceClip = new VoiceClip(jsonBytes);

                // Write to temporary file first
                string tempPath = $"{_metadataPath}.tmp";
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await voiceClip.CopyToAsync(fileStream, 128 * 1024, cancellationToken);

                // Delete existing file if it exists
                if (await _localStorageService.FileExistsAsync(_metadataPath))
                {
                    await _localStorageService.DeleteFileAsync(_metadataPath);
                }

                // Rename temp file to target file
                File.Move(tempPath, _metadataPath);

                // Update cache
                await _cache.SetAsync(RedisKeys.MP3_METADATA_DB_KEY, mp3Files, DB_CACHE_DURATION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save MP3 files database");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _dbLock.Dispose();
                }
                _disposed = true;
            }
        }
    }
}