﻿namespace MyTts.Services.Interfaces
{
    public interface ICloudStorageProvider
    {
        Task<Stream?> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default);
        // Add other cloud storage methods as needed
    }
}