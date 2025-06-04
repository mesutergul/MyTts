using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyTts.Models;
using Polly;

namespace MyTts.Services.Interfaces
{
    public interface IMp3StreamMerger : IAsyncDisposable
    {
        Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            CancellationToken cancellationToken = default);
    }
}