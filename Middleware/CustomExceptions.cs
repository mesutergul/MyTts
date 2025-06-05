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

    /// <summary>
    /// Represents an error that occurs during storage operations (file system, cloud storage, etc.).
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class StorageException : AppException
    {
        public StorageException(string message) : base(message) { }
        public StorageException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when rate limits are exceeded.
    /// Maps to HTTP 429 Too Many Requests.
    /// </summary>
    public class RateLimitExceededException : AppException
    {
        public RateLimitExceededException(string message) : base(message) { }
        public RateLimitExceededException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents a database connection error.
    /// Maps to HTTP 503 Service Unavailable.
    /// </summary>
    public class DatabaseConnectionException : AppException
    {
        public DatabaseConnectionException(string message) : base(message) { }
        public DatabaseConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents a database timeout error.
    /// Maps to HTTP 504 Gateway Timeout.
    /// </summary>
    public class DatabaseTimeoutException : AppException
    {
        public DatabaseTimeoutException(string message) : base(message) { }
        public DatabaseTimeoutException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents a database deadlock error.
    /// Maps to HTTP 409 Conflict.
    /// </summary>
    public class DatabaseDeadlockException : AppException
    {
        public DatabaseDeadlockException(string message) : base(message) { }
        public DatabaseDeadlockException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents a database constraint violation error.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class DatabaseConstraintException : AppException
    {
        public DatabaseConstraintException(string message) : base(message) { }
        public DatabaseConstraintException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during Entity Framework operations.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class EntityFrameworkException : AppException
    {
        public EntityFrameworkException(string message) : base(message) { }
        public EntityFrameworkException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an entity is not found in the database.
    /// Maps to HTTP 404 Not Found.
    /// </summary>
    public class EntityNotFoundException : AppException
    {
        public EntityNotFoundException(string message) : base(message) { }
        public EntityNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when there's a concurrency conflict in Entity Framework.
    /// Maps to HTTP 409 Conflict.
    /// </summary>
    public class EntityConcurrencyException : AppException
    {
        public EntityConcurrencyException(string message) : base(message) { }
        public EntityConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when there's a validation failure in Entity Framework.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class EntityValidationException : AppException
    {
        public EntityValidationException(string message) : base(message) { }
        public EntityValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during dependency injection operations.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class DependencyInjectionException : AppException
    {
        public DependencyInjectionException(string message) : base(message) { }
        public DependencyInjectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a required service is not registered in the DI container.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class ServiceNotRegisteredException : AppException
    {
        public ServiceNotRegisteredException(string message) : base(message) { }
        public ServiceNotRegisteredException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when there are circular dependencies in the DI container.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class CircularDependencyException : AppException
    {
        public CircularDependencyException(string message) : base(message) { }
        public CircularDependencyException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when there are multiple implementations registered for a service.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class AmbiguousServiceException : AppException
    {
        public AmbiguousServiceException(string message) : base(message) { }
        public AmbiguousServiceException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during notification operations.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class NotificationException : AppException
    {
        public NotificationException(string message) : base(message) { }
        public NotificationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a notification template is not found.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class NotificationTemplateNotFoundException : AppException
    {
        public NotificationTemplateNotFoundException(string message) : base(message) { }
        public NotificationTemplateNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a notification recipient is invalid.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class InvalidNotificationRecipientException : AppException
    {
        public InvalidNotificationRecipientException(string message) : base(message) { }
        public InvalidNotificationRecipientException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during email operations.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class EmailException : AppException
    {
        public EmailException(string message) : base(message) { }
        public EmailException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an email template is not found.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class EmailTemplateNotFoundException : AppException
    {
        public EmailTemplateNotFoundException(string message) : base(message) { }
        public EmailTemplateNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an email recipient is invalid.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class InvalidEmailRecipientException : AppException
    {
        public InvalidEmailRecipientException(string message) : base(message) { }
        public InvalidEmailRecipientException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an email attachment is invalid or too large.
    /// Maps to HTTP 400 Bad Request.
    /// </summary>
    public class InvalidEmailAttachmentException : AppException
    {
        public InvalidEmailAttachmentException(string message) : base(message) { }
        public InvalidEmailAttachmentException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during authentication.
    /// Maps to HTTP 401 Unauthorized.
    /// </summary>
    public class AuthenticationException : AppException
    {
        public AuthenticationException(string message) : base(message) { }
        public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when authentication credentials are invalid.
    /// Maps to HTTP 401 Unauthorized.
    /// </summary>
    public class InvalidCredentialsException : AppException
    {
        public InvalidCredentialsException(string message) : base(message) { }
        public InvalidCredentialsException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an authentication token is invalid or expired.
    /// Maps to HTTP 401 Unauthorized.
    /// </summary>
    public class InvalidTokenException : AppException
    {
        public InvalidTokenException(string message) : base(message) { }
        public InvalidTokenException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when an authentication token has expired.
    /// Maps to HTTP 401 Unauthorized.
    /// </summary>
    public class TokenExpiredException : AppException
    {
        public TokenExpiredException(string message) : base(message) { }
        public TokenExpiredException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during authorization.
    /// Maps to HTTP 403 Forbidden.
    /// </summary>
    public class AuthorizationException : AppException
    {
        public AuthorizationException(string message) : base(message) { }
        public AuthorizationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a user lacks required permissions.
    /// Maps to HTTP 403 Forbidden.
    /// </summary>
    public class InsufficientPermissionsException : AppException
    {
        public InsufficientPermissionsException(string message) : base(message) { }
        public InsufficientPermissionsException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a user's account is locked.
    /// Maps to HTTP 403 Forbidden.
    /// </summary>
    public class AccountLockedException : AppException
    {
        public AccountLockedException(string message) : base(message) { }
        public AccountLockedException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when a user's account is disabled.
    /// Maps to HTTP 403 Forbidden.
    /// </summary>
    public class AccountDisabledException : AppException
    {
        public AccountDisabledException(string message) : base(message) { }
        public AccountDisabledException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when the email service is unavailable.
    /// Maps to HTTP 503 Service Unavailable.
    /// </summary>
    public class EmailServiceUnavailableException : EmailException
    {
        public EmailServiceUnavailableException(string message) : base(message) { }
        public EmailServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs when email storage allocation is exceeded.
    /// Maps to HTTP 507 Insufficient Storage.
    /// </summary>
    public class EmailStorageException : EmailException
    {
        public EmailStorageException(string message) : base(message) { }
        public EmailStorageException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during an email transaction.
    /// Maps to HTTP 500 Internal Server Error.
    /// </summary>
    public class EmailTransactionException : EmailException
    {
        public EmailTransactionException(string message) : base(message) { }
        public EmailTransactionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Represents an error that occurs during email authentication.
    /// Maps to HTTP 401 Unauthorized.
    /// </summary>
    public class EmailAuthenticationException : EmailException
    {
        public EmailAuthenticationException(string message) : base(message) { }
        public EmailAuthenticationException(string message, Exception innerException) : base(message, innerException) { }
    }

    // Add more custom exceptions as needed for your domain (e.g., UnauthorizedException, ServiceUnavailableException)
}