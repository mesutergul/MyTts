// In Middleware/ErrorHandlerMiddleware.cs
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MyTts.Models; // Assuming ErrorDetails is in MyTts.Models
using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyTts.Middleware
{
    public class ErrorHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlerMiddleware> _logger;

        public ErrorHandlerMiddleware(RequestDelegate next, ILogger<ErrorHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            var response = context.Response;

            var errorDetails = new ErrorDetails
            {
                StatusCode = (int)HttpStatusCode.InternalServerError, // Default to 500
                Message = "An internal server error occurred. Please try again later.", // Generic message
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier
            };

            // Customize based on exception type if needed
            switch (exception)
            {
                case ApplicationException e: // Example: A custom application exception
                    // Potentially use a different status code or message for specific app exceptions
                    errorDetails.StatusCode = (int)HttpStatusCode.BadRequest; // Or another relevant code
                    errorDetails.Message = e.Message;
                        _logger.LogWarning(e, "Application exception caught for request {Path}. TraceId: {TraceId}", context.Request.Path, errorDetails.TraceId);
                    break;
                case UnauthorizedAccessException e:
                    errorDetails.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorDetails.Message = "Unauthorized access.";
                         _logger.LogWarning(e, "Unauthorized access caught for request {Path}. TraceId: {TraceId}", context.Request.Path, errorDetails.TraceId);
                    break;
                case KeyNotFoundException e: // Example for 404
                    errorDetails.StatusCode = (int)HttpStatusCode.NotFound;
                    errorDetails.Message = "The requested resource was not found.";
                        _logger.LogWarning(e, "Resource not found for request {Path}. TraceId: {TraceId}", context.Request.Path, errorDetails.TraceId);
                    break;
                default:
                    // For unhandled exceptions, log as error
                        _logger.LogError(exception, "Unhandled exception caught for request {Path}. TraceId: {TraceId}", context.Request.Path, errorDetails.TraceId);
                    break;
            }

            response.StatusCode = errorDetails.StatusCode;
            await response.WriteAsync(JsonSerializer.Serialize(errorDetails, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }
}
