using Newtonsoft.Json;
using MyTts.Data.Interfaces;
using System.Text;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Services.Constants;
using MyTts.Helpers;
using MyTts.Storage.Interfaces;
using System.IO;

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
        private readonly ILocalStorageClient _storage;
        private static readonly TimeSpan DB_CACHE_DURATION = RedisKeys.DB_CACHE_DURATION;
        private static readonly TimeSpan FILE_CACHE_DURATION = RedisKeys.FILE_CACHE_DURATION;
        private readonly Task _initializationTask;

        public static string GetStorageKey(int id) => StoragePathHelper.GetStorageKey(id);
        public static string GetCacheKey(int id) => RedisKeys.FormatKey(RedisKeys.MP3_FILE_KEY, id);

        public Mp3Repository(
            ILogger<Mp3Repository> logger,
            ILocalStorageClient storage,
            IRedisCacheService cache,
            IMp3MetaRepository mp3MetaRepository,
            INewsRepository newsRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            
            _baseStoragePath = StoragePathHelper.GetBasePath();
            _metadataPath = StoragePathHelper.GetMetadataPath();
            
            _dbLock = new SemaphoreSlim(1, 1);
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _mp3MetaRepository = mp3MetaRepository ?? throw new ArgumentNullException(nameof(mp3MetaRepository));
            _newsRepository = newsRepository ?? throw new ArgumentNullException(nameof(newsRepository));
            
            // Start initialization asynchronously
            _initializationTask = Task.Run(async () =>
            {
                try
                {
                    await InitializeDirectoriesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize directories during startup");
                    // Don't throw here - we'll handle initialization failures when operations are attempted
                }
            });
        }

        private async Task EnsureInitializedAsync()
        {
            try
            {
                await _initializationTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Directory initialization failed");
                throw new InvalidOperationException("Failed to initialize storage directories", ex);
            }
        }

        public async Task<bool> Mp3FileExistsInCacheAsync(string cacheKey, CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync();
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
                _logger.LogInformation("File with id : {id} exists in disk", id);
                if (!await Mp3FileExistsInCacheAsync(cacheKey, cancellationToken))
                {
                    await _cache.SetAsync(cacheKey, true, FILE_CACHE_DURATION);
                }
                //if (!await Mp3FileExistsInSqlAsync(id, cancellationToken))
                //{
                //    await SaveMp3MetaToSql(new Mp3Dto { FileId = id, FileUrl = StoragePathHelper.GetFullPathById(id, fileType), Language = language }, cancellationToken);
                //}
            }
            else _logger.LogInformation("File not found in cache, database, or disk for ID {Id}", id);
            return existsInDisk;
        }

        public async Task<bool> Mp3FileExistsAsync(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            string fullPath = StoragePathHelper.GetFullPathById(id, fileType);
            _logger.LogDebug("Checking if file exists at {FullPath}", fullPath);
            var result = await _storage.FileExistsAsync(fullPath, cancellationToken);
            return result.IsSuccess && result.Data;
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
            string fullPath = StoragePathHelper.GetFullPathById(filePath, fileType);
            try
            {
                var existResult = await _storage.FileExistsAsync(fullPath, cancellationToken);
                if (!existResult.IsSuccess || !existResult.Data)
                {
                    _logger.LogWarning("File not found or empty at path: {Path}", fullPath);
                    return Array.Empty<byte>();
                }

                var fileInfoResult = await _storage.GetFileInfoAsync(fullPath, cancellationToken);
                if (!fileInfoResult.IsSuccess)
                {
                    throw fileInfoResult.Error!.Exception;
                }

                var fileInfo = fileInfoResult.Data!;
                if (fileInfo.Length > 100 * 1024 * 1024) // 100MB threshold
                {
                    var streamResult = await _storage.ReadLargeFileAsStreamAsync(fullPath, cancellationToken);
                    if (!streamResult.IsSuccess)
                    {
                        throw streamResult.Error!.Exception;
                    }
                    using var memoryStream = new MemoryStream();
                    await streamResult.Data!.CopyToAsync(memoryStream, cancellationToken);
                    return memoryStream.ToArray();
                }
                else
                {
                    var bytesResult = await _storage.ReadAllBytesAsync(fullPath, cancellationToken);
                    if (!bytesResult.IsSuccess)
                    {
                        throw bytesResult.Error!.Exception;
                    }
                    return bytesResult.Data!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file from disk: {Path}", fullPath);
                throw new IOException($"Failed to read file: {fullPath}", ex);
            }
        }

        public async Task<Stream> ReadLargeFileAsStreamAsync(int id, string language, int bufferSize, AudioType fileType, bool isMerged, CancellationToken cancellationToken)
        {
            string fullPath = isMerged 
                ? StoragePathHelper.GetFullPath("merged", fileType)
                : StoragePathHelper.GetFullPathById(id, fileType);

            var existResult = await _storage.FileExistsAsync(fullPath, cancellationToken);
            if (!existResult.IsSuccess || !existResult.Data)
            {
                _logger.LogError("File not found: " + fullPath);
                throw new FileNotFoundException("Cannot open file: " + fullPath);
            }

            var result = await _storage.ReadLargeFileAsStreamAsync(fullPath, cancellationToken);
            if (!result.IsSuccess)
            {
                throw result.Error!.Exception;
            }

            return result.Data!;
        }

        public async Task SaveMp3FileAsync(int filePath, byte[] fileData, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fileData);

            string cacheKey = GetCacheKey(filePath);
            string fullPath = StoragePathHelper.GetFullPathById(filePath, fileType);

            try
            {
                string directory = Path.GetDirectoryName(fullPath)!;
                var dirExistsResult = await _storage.DirectoryExistsAsync(directory, cancellationToken);
                if (!dirExistsResult.IsSuccess || !dirExistsResult.Data)
                {
                    var createDirResult = await _storage.CreateDirectoryAsync(directory, cancellationToken);
                    if (!createDirResult.IsSuccess)
                    {
                        throw createDirResult.Error!.Exception;
                    }
                }

                var writeResult = await _storage.WriteAllBytesAsync(fullPath, fileData, cancellationToken);
                if (!writeResult.IsSuccess)
                {
                    throw writeResult.Error!.Exception;
                }

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
                int id = int.Parse(filePath);
                string cacheKey = GetCacheKey(id);
                string fullPath = StoragePathHelper.GetFullPathById(id, AudioType.Mp3);

                var result = await _storage.DeleteFileAsync(fullPath, cancellationToken);
                if (!result.IsSuccess)
                {
                    throw result.Error!.Exception;
                }

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
                // Create base storage directory if it doesn't exist
                var baseDirExistsResult = await _storage.DirectoryExistsAsync(_baseStoragePath);
                if (!baseDirExistsResult.IsSuccess || !baseDirExistsResult.Data)
                {
                    _logger.LogInformation("Creating base storage directory: {Path}", _baseStoragePath);
                    var createResult = await _storage.CreateDirectoryAsync(_baseStoragePath);
                    if (!createResult.IsSuccess)
                    {
                        _logger.LogError(createResult.Error!.Exception, "Failed to create base storage directory: {Path}", _baseStoragePath);
                        throw new InvalidOperationException($"Failed to create base storage directory: {_baseStoragePath}", createResult.Error!.Exception);
                    }
                }

                // Create metadata directory if it doesn't exist
                var metadataDir = Path.GetDirectoryName(_metadataPath);
                if (!string.IsNullOrEmpty(metadataDir))
                {
                    var metaDirExistsResult = await _storage.DirectoryExistsAsync(metadataDir);
                    if (!metaDirExistsResult.IsSuccess || !metaDirExistsResult.Data)
                    {
                        _logger.LogInformation("Creating metadata directory: {Path}", metadataDir);
                        var createResult = await _storage.CreateDirectoryAsync(metadataDir);
                        if (!createResult.IsSuccess)
                        {
                            _logger.LogError(createResult.Error!.Exception, "Failed to create metadata directory: {Path}", metadataDir);
                            throw new InvalidOperationException($"Failed to create metadata directory: {metadataDir}", createResult.Error!.Exception);
                        }
                    }
                }

                // Verify access to directories
                try
                {
                    await VerifyDirectoryAccessAsync(_baseStoragePath);
                    if (!string.IsNullOrEmpty(metadataDir))
                    {
                        await VerifyDirectoryAccessAsync(metadataDir);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but don't throw - we don't want to block initialization if disk space check fails
                    _logger.LogWarning(ex, "Directory access verification failed, but continuing initialization");
                }

                // Create language-specific directories
                var languages = new[] { "tr", "en", "ar", "ru" };
                foreach (var lang in languages)
                {
                    var langPath = Path.Combine(_baseStoragePath, lang);
                    var langDirExistsResult = await _storage.DirectoryExistsAsync(langPath);
                    if (!langDirExistsResult.IsSuccess || !langDirExistsResult.Data)
                    {
                        _logger.LogInformation("Creating language directory: {Path}", langPath);
                        var createResult = await _storage.CreateDirectoryAsync(langPath);
                        if (!createResult.IsSuccess)
                        {
                            _logger.LogError(createResult.Error!.Exception, "Failed to create language directory: {Path}", langPath);
                            throw new InvalidOperationException($"Failed to create language directory: {langPath}", createResult.Error!.Exception);
                        }
                    }
                }

                _logger.LogInformation("Directory initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize directories");
                throw;
            }
        }

        public async Task VerifyDirectoryAccessAsync(string path)
        {
            try
            {
                // Skip drive info check for network paths
                if (path.StartsWith(@"\\"))
                {
                    _logger.LogDebug("Skipping drive info check for network path: {Path}", path);
                    return;
                }

                var rootPath = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(rootPath))
                {
                    _logger.LogWarning("Could not determine root path for: {Path}", path);
                    return;
                }

                // Ensure the drive path is in the correct format (e.g., "C:\")
                var driveLetter = rootPath.TrimEnd('\\');
                if (driveLetter.Length != 2 || !char.IsLetter(driveLetter[0]) || driveLetter[1] != ':')
                {
                    _logger.LogWarning("Invalid drive path format: {Path}", rootPath);
                    return;
                }

                var driveInfo = new DriveInfo(driveLetter);
                if (!driveInfo.IsReady)
                {
                    _logger.LogWarning("Drive {Drive} is not ready", driveInfo.Name);
                    return;
                }

                var freeSpacePercentage = 1.0 - ((double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize);
                if (freeSpacePercentage >= 0.95) // 95% full
                {
                    _logger.LogError(
                        "Disk space critically low. Drive: {Drive}, Free Space: {FreeSpace}GB, Total Space: {TotalSpace}GB, Usage: {UsagePercentage}%",
                        driveInfo.Name,
                        driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024),
                        driveInfo.TotalSize / (1024.0 * 1024 * 1024),
                        freeSpacePercentage * 100);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - we don't want to block operations if disk space check fails
                _logger.LogWarning(ex, "An unexpected error occurred while verifying directory access for '{Path}'.", path);
            }
        }

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
        public async Task<Mp3Dto> LoadMp3MetaByPathAsync(string filePath, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                var mp3File = await _mp3MetaRepository.GetByColumnAsync(x => x.FileUrl == filePath, cancellationToken)
                    ?? throw new KeyNotFoundException($"MP3 file not found for path: {filePath}");
                return mp3File;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load MP3 metadata for path: {Path}", filePath);
                throw;
            }
        }
        public async Task<Mp3Dto> LoadLatestMp3MetaByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3MetaRepository.GetByColumnAsync(x=>x.Language==language, cancellationToken);
        }

        public async Task SaveSingleMp3MetaAsync(Mp3Dto mp3File, AudioType fileType, CancellationToken cancellationToken)
        {
            await _mp3MetaRepository.AddAsync(mp3File, cancellationToken);
        }
        public async Task<Mp3Dto> LoadAndCacheMp3File(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            var mp3File = await LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken);
            string cacheKey = GetCacheKey(id);
            _logger.LogDebug("Caching metadata for ID {Id} with key {CacheKey} for {Duration} hours",
                id, cacheKey, RedisKeys.DEFAULT_METADATA_EXPIRY.TotalHours);
            await SetToCacheAsync(cacheKey, mp3File, RedisKeys.DEFAULT_METADATA_EXPIRY, cancellationToken);
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
                var existsResult = await _storage.FileExistsAsync(_metadataPath, cancellationToken);
                if (!existsResult.IsSuccess || !existsResult.Data)
                {
                    var emptyList = new List<Mp3Dto>();
                    _logger.LogInformation("Caching empty metadata list as file does not exist");
                    await _cache.SetAsync(RedisKeys.MP3_METADATA_DB_KEY, emptyList, DB_CACHE_DURATION);
                    return emptyList;
                }

                // Read the file using LocalStorageService
                var readResult = await _storage.ReadAllTextAsync(_metadataPath, cancellationToken);
                if (!readResult.IsSuccess)
                {
                    throw readResult.Error!.Exception;
                }

                var mp3Files = JsonConvert.DeserializeObject<List<Mp3Dto>>(readResult.Data!, _jsonSettings)
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

        public async Task SaveMp3MetadataToSqlBatchAsync(List<Mp3Dto> newMp3Files, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(newMp3Files);

            if (_disposed)
            {
                _logger.LogWarning("Repository is disposed, cannot save metadata");
                return;
            }

            try
            {
                await _dbLock.WaitAsync(cancellationToken);
                try
                {
                    // Save to SQL database in batch
                    await _mp3MetaRepository.AddRangeAsync(newMp3Files, cancellationToken);
                    _logger.LogInformation("Successfully saved {Count} files to SQL", newMp3Files.Count);
                }
                finally
                {
                    if (!_disposed)
                    {
                        _dbLock.Release();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Repository was disposed during save operation");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save MP3 files to SQL database");
                throw;
            }
        }

        public async Task SaveMp3MetadataToJsonAndCacheAsync(List<Mp3Dto> mp3Files, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(mp3Files);

            await _dbLock.WaitAsync();
            try
            {
                // Save to JSON file
                var json = JsonConvert.SerializeObject(mp3Files, Formatting.Indented);
                string tempPath = $"{_metadataPath}.tmp";
                var writeResult = await _storage.WriteAllTextAsync(tempPath, json, cancellationToken);
                if (!writeResult.IsSuccess)
                {
                    throw writeResult.Error!.Exception;
                }

                // Delete existing file if it exists
                var existsResult = await _storage.FileExistsAsync(_metadataPath, cancellationToken);
                if (existsResult.IsSuccess && existsResult.Data)
                {
                    var deleteResult = await _storage.DeleteFileAsync(_metadataPath, cancellationToken);
                    if (!deleteResult.IsSuccess)
                    {
                        throw deleteResult.Error!.Exception;
                    }
                }

                // Rename temp file to target file
                var moveResult = await _storage.MoveFileAsync(tempPath, _metadataPath, cancellationToken);
                if (!moveResult.IsSuccess)
                {
                    throw moveResult.Error!.Exception;
                }

                // Update cache
                await _cache.SetAsync(RedisKeys.MP3_METADATA_DB_KEY, mp3Files, DB_CACHE_DURATION);

                _logger.LogInformation("Successfully saved {Count} files to JSON and cache", mp3Files.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save MP3 files to JSON/cache");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }
        public async Task<Dictionary<int, string>> GetExistingHashList(List<int> myList, CancellationToken cancellationToken)
        {
            try
            {
                await _dbLock.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogDebug("Loading MP3 files from database");
                    return await _mp3MetaRepository.GetExistingHashesAsync(myList, cancellationToken);
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
                    try
                    {
                        // Wait for any pending operations to complete
                        if (_dbLock.CurrentCount == 0)
                        {
                            _logger.LogWarning("Disposing repository while operations are pending");
                        }
                        _dbLock.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing repository");
                    }
                }
                _disposed = true;
            }
        }
    }
}