using System;

namespace MyTts.Storage.Models
{
    public class StorageResult<T>
    {
        public bool IsSuccess { get; }
        public T? Data { get; }
        public StorageError? Error { get; }
        public TimeSpan OperationTime { get; }

        private StorageResult(bool isSuccess, T? data, StorageError? error, TimeSpan operationTime)
        {
            IsSuccess = isSuccess;
            Data = data;
            Error = error;
            OperationTime = operationTime;
        }

        public static StorageResult<T> Success(T data, TimeSpan operationTime)
            => new(true, data, null, operationTime);

        public static StorageResult<T> Failure(StorageError error, TimeSpan operationTime)
            => new(false, default, error, operationTime);
    }

    public class StorageResult
    {
        public bool IsSuccess { get; }
        public StorageError? Error { get; }
        public TimeSpan OperationTime { get; }

        private StorageResult(bool isSuccess, StorageError? error, TimeSpan operationTime)
        {
            IsSuccess = isSuccess;
            Error = error;
            OperationTime = operationTime;
        }

        public static StorageResult Success(TimeSpan operationTime)
            => new(true, null, operationTime);

        public static StorageResult Failure(StorageError error, TimeSpan operationTime)
            => new(false, error, operationTime);
    }

    public record StorageError(Exception Exception, string Message = "")
    {
        public string Message { get; } = string.IsNullOrEmpty(Message) ? Exception.Message : Message;
    }
} 