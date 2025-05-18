using MyTts.Storage;
using MyTts.Models;

namespace MyTts.Helpers
{
    public static class StoragePathHelper
    {
        private static StorageConfiguration? _storageConfig;
        private const string STORAGE_PREFIX_KEY = "speech_";

        public static void Initialize(StorageConfiguration storageConfig)
        {
            _storageConfig = storageConfig ?? throw new ArgumentNullException(nameof(storageConfig));
        }

        private static void EnsureInitialized()
        {
            if (_storageConfig == null)
                throw new InvalidOperationException("StoragePathHelper has not been initialized. Call Initialize() first.");
        }

        public static string GetStorageKey(int id)
        {
            if (id <= 0)
                throw new ArgumentException("ID must be greater than zero", nameof(id));

            return $"{STORAGE_PREFIX_KEY}{id}";
        }

        public static string GetFullPath(string filePath, AudioType fileType = AudioType.Mp3)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            return Path.Combine(_storageConfig!.GetNormalizedBasePath(), filePath + "." + fileType.ToString().ToLower());
        }

        public static string GetFullPathById(int id, AudioType fileType = AudioType.Mp3)
        {
            return GetFullPath(GetStorageKey(id), fileType);
        }

        public static string GetBasePath()
        {
            EnsureInitialized();
            return _storageConfig!.GetNormalizedBasePath();
        }

        public static string GetMetadataPath()
        {
            EnsureInitialized();
            return _storageConfig!.GetNormalizedMetadataPath();
        }
    }
} 