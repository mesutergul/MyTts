using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; // For ProblemDetails
using Microsoft.Extensions.Hosting; // For IHostEnvironment
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyTts.Middleware
{
    public class ErrorHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlerMiddleware> _logger;
        private readonly IHostEnvironment _env;
        private readonly Dictionary<Type, HttpStatusCode> _exceptionStatusCodeMap;

        public ErrorHandlerMiddleware(
            RequestDelegate next,
            ILogger<ErrorHandlerMiddleware> logger,
            IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;

            // Define default mappings for common exceptions
            _exceptionStatusCodeMap = new Dictionary<Type, HttpStatusCode>
            {
                { typeof(NotFoundException), HttpStatusCode.NotFound },
                { typeof(BadRequestException), HttpStatusCode.BadRequest },
                { typeof(ForbiddenException), HttpStatusCode.Forbidden },
                { typeof(ConflictException), HttpStatusCode.Conflict },
                { typeof(UnauthorizedAccessException), HttpStatusCode.Forbidden }, // For explicit throws
                { typeof(ArgumentException), HttpStatusCode.BadRequest },
                { typeof(InvalidOperationException), HttpStatusCode.InternalServerError }, // Changed from BadRequest to InternalServerError
                { typeof(TaskCanceledException), HttpStatusCode.ServiceUnavailable }, // Client disconnected or operation cancelled
                { typeof(TimeoutException), HttpStatusCode.GatewayTimeout }, // Outgoing service call timeout
                { typeof(HttpRequestException), HttpStatusCode.BadGateway }, // Outgoing HTTP call failure
                { typeof(IOException), HttpStatusCode.InternalServerError }, // File/network I/O issues
                { typeof(BadHttpRequestException), HttpStatusCode.BadRequest } // Malformed incoming request
            };
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context); // Call the next middleware in the pipeline
            }
            catch (Exception ex)
            {
                // Log the exception details
                LogException(context, ex);

                // Handle the exception and generate a ProblemDetails response
                await HandleExceptionAsync(context, ex);
            }
        }

        private void LogException(HttpContext context, Exception ex)
        {
            // Get the correlation ID (TraceIdentifier) for logging
            var traceId = context.TraceIdentifier;

            // Determine log level based on exception type
            if (_exceptionStatusCodeMap.TryGetValue(ex.GetType(), out var statusCode) && (int)statusCode < 500)
            {
                _logger.LogWarning(ex, "Client-side error caught by middleware. TraceId: {TraceId}. Path: {Path}. Message: {Message}",
                    traceId, context.Request.Path, ex.Message);
            }
            else
            {
                _logger.LogError(ex, "Server-side error caught by middleware. TraceId: {TraceId}. Path: {Path}. Message: {Message}",
                    traceId, context.Request.Path, ex.Message);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Determine the HTTP status code
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError; // Default to 500
            if (_exceptionStatusCodeMap.TryGetValue(exception.GetType(), out var mappedStatusCode))
            {
                statusCode = mappedStatusCode;
            }

            // Set response content type and status code
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = (int)statusCode;

            // Create ProblemDetails object
            var problemDetails = new ProblemDetails
            {
                Status = (int)statusCode,
                Title = GetProblemTitle(statusCode),
                Detail = "An error occurred while processing your request.", // Generic message for production
                Instance = context.Request.Path, // The path to the API endpoint
                Type = $"https://httpstatuses.com/{(int)statusCode}", // Link to HTTP status definition
                Extensions = new Dictionary<string, object?>
                {
                    { "traceId", context.TraceIdentifier }, // Correlation ID
                    { "exceptionType", exception.GetType().Name }
                }
            };

            // Conditionally add sensitive details in Development environment
            if (_env.IsDevelopment())
            {
                problemDetails.Detail = exception.Message; // Full exception message
                if (exception.StackTrace != null)
                {
                    problemDetails.Extensions.Add("stackTrace", exception.StackTrace);
                }
                if (exception.InnerException != null)
                {
                    problemDetails.Extensions.Add("innerExceptionMessage", exception.InnerException.Message);
                }
            }
            else
            {
                // In production, provide a generic message for internal server errors
                if (statusCode == HttpStatusCode.InternalServerError)
                {
                    problemDetails.Detail = "An unexpected error occurred. Please try again later.";
                }
            }

            // Serialize ProblemDetails to JSON and write to response
            var jsonOptions = new JsonSerializerOptions { WriteIndented = _env.IsDevelopment(), PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(problemDetails, jsonOptions);

            await context.Response.WriteAsync(json);
        }

        /// <summary>
        /// Provides a default title for ProblemDetails based on HTTP status code.
        /// </summary>
        private string GetProblemTitle(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.BadRequest => "Bad Request",
                HttpStatusCode.Unauthorized => "Unauthorized",
                HttpStatusCode.Forbidden => "Forbidden",
                HttpStatusCode.NotFound => "Not Found",
                HttpStatusCode.Conflict => "Conflict",
                HttpStatusCode.InternalServerError => "Internal Server Error",
                HttpStatusCode.BadGateway => "Bad Gateway",
                HttpStatusCode.GatewayTimeout => "Gateway Timeout",
                HttpStatusCode.ServiceUnavailable => "Service Unavailable",
                _ => "An Error Occurred"
            };
        }

        /// <summary>
        /// Allows external configuration to add or override exception-to-status-code mappings.
        /// </summary>
        public void AddExceptionMapping(Type exceptionType, HttpStatusCode statusCode)
        {
            if (!typeof(Exception).IsAssignableFrom(exceptionType))
            {
                throw new ArgumentException("Provided type must be an Exception.", nameof(exceptionType));
            }
            _exceptionStatusCodeMap[exceptionType] = statusCode;
        }
    }
}