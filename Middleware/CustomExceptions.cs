using System;

namespace MyTts.Middleware
{
    /// <summary>
    /// Base class for all custom application exceptions.
    /// </summary>
    public abstract class AppException : Exception
    {
        protected AppException(string message) : base(message) { }
        protected AppException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error where a requested resource was not found.
    /// Maps to HTTP 404 Not Found.
    /// </summary>
    public class NotFoundException : AppException
    {
        public NotFoundException(string message) : base(message) { }
        public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error due to invalid input or business rule violation.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class BadRequestException : AppException
    {
        public BadRequestException(string message) : base(message) { }
        public BadRequestException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error where an operation is forbidden due to insufficient permissions.
    /// Maps to HTTP 403 Forbidden.
    /// </summary>
    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message) : base(message) { }
        public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error where there's a conflict with the current state of the resource.
    /// Maps to HTTP 409 Conflict.
    /// </summary>
    public class ConflictException : AppException
    {
        public ConflictException(string message) : base(message) { }
        public ConflictException(string message, Exception innerException) : base(message, innerException) { }
    }

    // Add more custom exceptions as needed for your domain (e.g., UnauthorizedException, ServiceUnavailableException)
}